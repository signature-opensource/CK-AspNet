using CK.AspNet;
using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AppIdentity;

/// <summary>
/// The "HttpEndpoint" feature of the root party: its web application, configured by the "Local:HttpEndpoint" section,
/// started once the party is initialized and stopped before it (see <see cref="ApplicationIdentityStaticFeatureDriver"/>).
/// <para>
/// A local party has one endpoint: one origin, one pipeline. "Local:HttpEndpoint" has either "Urls" (separated by ';')
/// or a "Kestrel" section (Kestrel's own configuration: Endpoints, Certificates, Limits...): several listening addresses
/// don't need several pipelines. The application configures the services and the pipeline of the endpoint with
/// <see cref="HttpEndpointHostApplicationBuilderExtensions.ConfigureHttpEndpoint"/>.
/// </para>
/// <para>
/// The other keys of "Local:HttpEndpoint" are its options (<see cref="HttpEndpointOption"/>, like the
/// <see cref="ForwardedHeadersOption"/>): any other key is an error.
/// </para>
/// <para>
/// This is a static feature: only the root party can have an endpoint (a tenant domain that allows it is an error).
/// </para>
/// <para>
/// It contributes the "Http" section of the running status of a development party (the "DevRunningStatus" feature of
/// CK.AppIdentity): the "Urls" and the "DefaultUrl" of the <see cref="Endpoint"/> (see <see cref="RunningHttpEndpoint"/>).
/// The party is ready once the endpoint is started (or when this feature has nothing to start).
/// </para>
/// </summary>
public sealed class HttpEndpointFeatureDriver : ApplicationIdentityStaticFeatureDriver, IDevRunningStatusContributor
{
    /// <summary>
    /// The configuration key of the feature, in the "Local" section.
    /// </summary>
    public const string ConfigurationKey = "HttpEndpoint";

    static readonly IConfiguration _emptyConfiguration = new ConfigurationBuilder().Build();

    readonly HttpEndpointHost _host;
    readonly IServiceProvider _services;
    readonly PartyComponentOptionDirectory _optionDirectory;
    EndpointConfiguration? _configuration;
    // The started endpoint (stopped by the teardown if OnHostStoppingAsync has not been called).
    RunningHttpEndpoint? _endpoint;
    // The published endpoint: set once it is started, null when it stops.
    volatile RunningHttpEndpoint? _published;
    bool _stopping;

    // DefaultEndpointUrl: the configured "Url" of the Kestrel endpoint named by "DefaultEndpoint".
    // Options: the enabled options of the endpoint, initialized, in their order.
    sealed record EndpointConfiguration( string[]? Urls,
                                         IConfigurationSection? Kestrel,
                                         string? DefaultEndpointUrl,
                                         IReadOnlyList<HttpEndpointOption> Options );

    /// <summary>
    /// Initializes a new <see cref="HttpEndpointFeatureDriver"/>.
    /// </summary>
    /// <param name="s">The application identity service.</param>
    /// <param name="host">The application's configuration of the endpoint and the services of the host.</param>
    /// <param name="services">The services of the host.</param>
    /// <param name="optionDirectory">The options of the party components (the options of the endpoint).</param>
    public HttpEndpointFeatureDriver( ApplicationIdentityService s,
                                      HttpEndpointHost host,
                                      IServiceProvider services,
                                      PartyComponentOptionDirectory optionDirectory )
        : base( s, isAllowedByDefault: false )
    {
        _host = host;
        _services = services;
        _optionDirectory = optionDirectory;
    }

    /// <summary>
    /// Gets the running endpoint (null until the host is started and after it is stopped).
    /// This is thread safe.
    /// </summary>
    public RunningHttpEndpoint? Endpoint => _published;

    // Set by an active running status writer (dormant otherwise).
    Action? _onRunningStatusChanged;

    void IDevRunningStatusContributor.Activate( IActivityMonitor monitor, Action onChanged ) => _onRunningStatusChanged = onChanged;

    // Nothing to start (not allowed or the setup failed), or the endpoint is started.
    bool IDevRunningStatusContributor.IsReady => _configuration == null || _published != null;

    void IDevRunningStatusContributor.Write( Utf8JsonWriter writer )
    {
        var e = _published;
        if( e == null )
        {
            writer.WriteNull( "Http" );
            return;
        }
        writer.WriteStartObject( "Http" );
        writer.WriteStartArray( "Urls" );
        foreach( var u in e.Urls ) writer.WriteStringValue( u );
        writer.WriteEndArray();
        writer.WriteString( "DefaultUrl", e.DefaultUrl );
        writer.WriteEndObject();
    }

    /// <summary>
    /// Contributes the public URL of the root party when "Local:PublicUrl" is not configured: the
    /// <see cref="RunningHttpEndpoint.DefaultUrl"/> of the started endpoint (a port 0 is resolved). Behind a reverse
    /// proxy or a public domain name, "Local:PublicUrl" is configured.
    /// </summary>
    /// <param name="party">The local party to describe.</param>
    /// <param name="builder">The description builder.</param>
    protected override void ContributeDescription( ILocalParty party, PartyDescription.Builder builder )
    {
        if( party != ApplicationIdentityService || builder.PublicUrl != null ) return;
        if( _published?.DefaultUrl is { } defaultUrl
            && PartyDescription.TryParsePublicUrl( defaultUrl, out var url, out _ ) )
        {
            builder.PublicUrl = url;
        }
    }

    /// <inheritdoc />
    protected override Task<bool> SetupAsync( FeatureLifetimeContext context )
    {
        var monitor = context.Monitor;
        bool success = true;
        foreach( var local in context.GetAllLocals() )
        {
            if( local != ApplicationIdentityService && IsAllowedFeature( local ) )
            {
                monitor.Error( $"'{local}' allows the '{FeatureName}' feature: only the root party can have an HTTP endpoint." );
                success = false;
            }
        }
        if( !IsRootAllowed )
        {
            if( _host.Configurations.Count > 0 )
            {
                monitor.Warn( $"The '{FeatureName}' feature is not allowed (\"Local:AllowFeatures\"): the application's configuration of the endpoint is ignored." );
            }
            return Task.FromResult( success );
        }
        if( _host.HostServices == null )
        {
            monitor.Error( $"The services of the host are not available: the map doesn't contain '{typeof( HttpEndpointHostRegistration ):C}'." );
            return Task.FromResult( false );
        }
        var configuration = ReadConfiguration( monitor );
        if( configuration == null || !CheckAspNetDefaults( monitor ) ) return Task.FromResult( false );
        _configuration = configuration;
        return Task.FromResult( success );
    }

    EndpointConfiguration? ReadConfiguration( IActivityMonitor monitor )
    {
        const string path = $"Local:{ConfigurationKey}";
        var section = ApplicationIdentityService.LocalConfiguration.Configuration.TryGetSection( ConfigurationKey );
        if( section == null )
        {
            monitor.Error( $"The '{FeatureName}' feature is allowed but there is no \"{path}\" section." );
            return null;
        }
        var urls = ReadUrls( section );
        var kestrel = section.TryGetSection( "Kestrel" );
        if( (urls == null) == (kestrel == null) )
        {
            monitor.Error( $"\"{path}\" must have either \"Urls\" or a \"Kestrel\" section." );
            // The endpoint level is gone: a named endpoint is moved into "Local:HttpEndpoint".
            foreach( var named in section.GetChildren().Where( c => c["Urls"] != null || c.TryGetSection( "Urls" ) != null || c.TryGetSection( "Kestrel" ) != null ) )
            {
                monitor.Error( $"\"{named.Path}\" looks like a named endpoint: a local party has one endpoint, the endpoint level is removed. Move its content into \"{path}\"." );
            }
            return null;
        }
        if( TryReadDefaultEndpoint( monitor, section, urls, kestrel, out var defaultEndpointUrl )
            && TryCreateOptions( monitor, section, out var options ) )
        {
            return new EndpointConfiguration( urls, kestrel, defaultEndpointUrl, options );
        }
        return null;

        // "DefaultEndpoint" names the Kestrel endpoint whose URL is the RunningHttpEndpoint.DefaultUrl. With "Urls", the
        // first one is the default one: "DefaultEndpoint" would say it twice.
        static bool TryReadDefaultEndpoint( IActivityMonitor monitor,
                                            ImmutableConfigurationSection c,
                                            string[]? urls,
                                            ImmutableConfigurationSection? kestrel,
                                            out string? defaultEndpointUrl )
        {
            defaultEndpointUrl = null;
            var name = c["DefaultEndpoint"];
            if( string.IsNullOrWhiteSpace( name ) ) return true;
            if( urls != null )
            {
                monitor.Error( $"\"{path}:DefaultEndpoint\" is only for a \"Kestrel\" section: with \"Urls\", the first one is the default one." );
                return false;
            }
            defaultEndpointUrl = kestrel?.TryGetSection( "Endpoints" )?.TryGetSection( name )?["Url"];
            if( string.IsNullOrWhiteSpace( defaultEndpointUrl ) )
            {
                monitor.Error( $"\"{path}:DefaultEndpoint\" is '{name}': there is no \"{path}:Kestrel:Endpoints:{name}:Url\"." );
                return false;
            }
            return true;
        }

        static string[]? ReadUrls( ImmutableConfigurationSection c )
        {
            var value = c["Urls"];
            var urls = value != null
                        ? value.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries )
                        : c.TryGetSection( "Urls" )?.GetChildren().Select( u => u.Value ).Where( u => !string.IsNullOrWhiteSpace( u ) ).Select( u => u!.Trim() ).ToArray();
            return urls is { Length: > 0 } ? urls : null;
        }
    }

    // The options enabled by the "Local:HttpEndpoint" section, created from the host services and initialized. A key that is
    // neither a key of the endpoint nor an option is an error.
    bool TryCreateOptions( IActivityMonitor monitor, ImmutableConfigurationSection section, out IReadOnlyList<HttpEndpointOption> options )
    {
        options = [];
        var ownerType = typeof( HttpEndpointFeatureDriver );
        bool success = true;
        foreach( var k in section.GetChildren() )
        {
            if( k.Key is not ("Urls" or "Kestrel" or "DefaultEndpoint") && !_optionDirectory.IsOptionName( ownerType, k.Key ) )
            {
                var names = _optionDirectory.GetOptions( ownerType ).Select( o => o.Name ).Concatenate();
                monitor.Error( $"Unknown \"{k.Path}\": the keys of \"Local:{ConfigurationKey}\" are Urls, Kestrel, DefaultEndpoint and its options ({(names.Length > 0 ? names : "none")})." );
                success = false;
            }
        }
        if( !success || !_optionDirectory.TrySelect( monitor, ownerType, section, out var selected ) ) return false;
        var result = new List<HttpEndpointOption>( selected.Count );
        foreach( var (info, configuration) in selected )
        {
            if( info.Create( _services ) is not HttpEndpointOption o )
            {
                monitor.Error( $"The option '{info.OptionType:C}' of the '{FeatureName}' feature must specialize '{typeof( HttpEndpointOption ):C}'." );
                success = false;
            }
            else if( o.Initialize( monitor, section, configuration ) )
            {
                result.Add( o );
            }
            else
            {
                success = false;
            }
        }
        if( success ) options = result;
        return success;
    }

    // The endpoint is configured in "Local:HttpEndpoint" only: two places must not configure the same port.
    bool CheckAspNetDefaults( IActivityMonitor monitor )
    {
        var configuration = _services.GetService<IConfiguration>();
        var found = new List<string>();
        if( configuration != null )
        {
            foreach( var k in (string[])["urls", "http_ports", "https_ports"] )
            {
                if( !string.IsNullOrWhiteSpace( configuration[k] ) ) found.Add( $"the \"{k}\" configuration" );
            }
            if( configuration.GetSection( "Kestrel" ).Exists() ) found.Add( "the \"Kestrel\" configuration section" );
        }
        foreach( var v in (string[])["ASPNETCORE_URLS", "ASPNETCORE_HTTP_PORTS", "ASPNETCORE_HTTPS_PORTS"] )
        {
            if( !string.IsNullOrWhiteSpace( Environment.GetEnvironmentVariable( v ) ) ) found.Add( $"the {v} environment variable" );
        }
        if( found.Count == 0 ) return true;
        var message = $"The '{FeatureName}' feature configures the endpoint in \"Local:{ConfigurationKey}\": {found.Concatenate( " and " )} must be removed (they are ignored).";
        if( _services.GetService<IHostEnvironment>()?.IsDevelopment() is true )
        {
            monitor.Error( message );
            return false;
        }
        monitor.Warn( message );
        return true;
    }

    /// <inheritdoc />
    protected override async Task OnHostStartedAsync( IActivityMonitor monitor, CancellationToken cancel )
    {
        if( _configuration == null ) return;
        try
        {
            _endpoint = await StartEndpointAsync( monitor, _configuration, cancel ).ConfigureAwait( false );
            monitor.Info( $"{_endpoint} started." );
            _published = _endpoint;
            _onRunningStatusChanged?.Invoke();
            await OnDescriptionChangedAsync( monitor, ApplicationIdentityService ).ConfigureAwait( false );
        }
        catch
        {
            // The base doesn't call OnHostStoppingAsync when OnHostStartedAsync fails.
            await StopEndpointAsync( monitor, default ).ConfigureAwait( false );
            throw;
        }
    }

    async Task<RunningHttpEndpoint> StartEndpointAsync( IActivityMonitor monitor, EndpointConfiguration c, CancellationToken cancel )
    {
        var environment = _services.GetService<IHostEnvironment>();
        var builder = WebApplication.CreateSlimBuilder( new WebApplicationOptions
        {
            ApplicationName = environment?.ApplicationName,
            EnvironmentName = environment?.EnvironmentName,
            ContentRootPath = environment?.ContentRootPath
        } );
        // The configuration is the host's one (with its overrides): the web application reads nothing by itself.
        builder.Configuration.Sources.Clear();
        var configuration = _services.GetService<IConfiguration>();
        if( configuration != null ) builder.Configuration.AddConfiguration( configuration );
        builder.WebHost.UseKestrelHttpsConfiguration();
        // The endpoint's Kestrel section replaces the binding of the root "Kestrel" section.
        var kestrel = (IConfiguration?)c.Kestrel ?? _emptyConfiguration;
        builder.WebHost.ConfigureKestrel( o => o.Configure( kestrel ) );
        if( c.Urls != null ) builder.WebHost.UseUrls( c.Urls );
        // The host handles the console (Ctrl+C) and the process lifetime, not the web application.
        builder.Services.Replace( ServiceDescriptor.Singleton<IHostLifetime, NoHostLifetime>() );
        HostServiceForwarder.Forward( monitor, _host.HostServices!, _services, builder.Services );
        // The packages' contributions (registered by their real objects), then the application's configuration.
        foreach( var o in c.Options ) o.ConfigureServices( builder );
        _services.GetService<WebApplicationContributions>()?.ApplyTo( builder );
        foreach( var configure in _host.Configurations ) configure( builder );
        // The middlewares of the options are the first ones, in their order: the last prepended action runs first.
        if( c.Options.Count > 0 )
        {
            builder.PrependApplicationBuilder( app =>
            {
                foreach( var o in c.Options ) o.ConfigurePipeline( app );
            }, beforeCKMiddleware: true );
        }
        var app = builder.CKBuild();
        // A party lives and dies with its endpoint: a web application that stops by itself stops the host.
        app.Lifetime.ApplicationStopping.Register( OnApplicationStopping );
        await app.StartAsync( cancel ).ConfigureAwait( false );
        return new RunningHttpEndpoint( app, configuredByUrls: c.Urls != null, c.DefaultEndpointUrl );
    }

    void OnApplicationStopping()
    {
        if( _stopping ) return;
        var lifetime = _services.GetService<IHostApplicationLifetime>();
        if( lifetime != null && !lifetime.ApplicationStopping.IsCancellationRequested )
        {
            ActivityMonitor.StaticLogger.Warn( $"The web application of the HTTP endpoint stops: the host is stopped." );
            lifetime.StopApplication();
        }
    }

    /// <summary>
    /// Raises the host's <see cref="IHostApplicationLifetime.ApplicationStopping"/>, then stops the endpoint.
    /// <para>
    /// The host raises it after the <see cref="IHostedLifecycleService.StoppingAsync"/> of its services, where the static
    /// features stop: without this, what aborts its long running requests on <c>ApplicationStopping</c> (the WebSocket
    /// channel for instance) would do it after the endpoint has drained them for its whole shutdown timeout. A web
    /// application raises it before its server drains: the endpoint keeps this order.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>The awaitable.</returns>
    protected override Task OnHostStoppingAsync( IActivityMonitor monitor, CancellationToken cancel )
    {
        _services.GetService<IHostApplicationLifetime>()?.StopApplication();
        return StopEndpointAsync( monitor, cancel );
    }

    // The web application is stopped, never disposed: its container would dispose the singletons forwarded to the host
    // (a container disposes what its factories return). Its lifetime is the host's.
    async Task StopEndpointAsync( IActivityMonitor monitor, CancellationToken cancel )
    {
        _stopping = true;
        if( _published != null )
        {
            _published = null;
            _onRunningStatusChanged?.Invoke();
            await OnDescriptionChangedAsync( monitor, ApplicationIdentityService ).ConfigureAwait( false );
        }
        var e = _endpoint;
        if( e == null ) return;
        _endpoint = null;
        try
        {
            await e.Application.StopAsync( cancel ).ConfigureAwait( false );
            monitor.Info( $"{e} stopped." );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While stopping {e}.", ex );
        }
    }

    /// <inheritdoc />
    protected override async Task TeardownAsync( FeatureLifetimeContext context )
    {
        if( _endpoint != null )
        {
            context.Monitor.Warn( $"The endpoint is still running at the teardown (OnHostStoppingAsync has not been called): it is stopped now." );
            await StopEndpointAsync( context.Monitor, default ).ConfigureAwait( false );
        }
    }

    sealed class NoHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync( CancellationToken cancellationToken ) => Task.CompletedTask;

        public Task StopAsync( CancellationToken cancellationToken ) => Task.CompletedTask;
    }
}
