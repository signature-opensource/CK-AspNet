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
using System.Threading;
using System.Threading.Tasks;

namespace CK.AppIdentity;

/// <summary>
/// The "HttpEndpoint" feature of the root party: one web application per endpoint configured in the "Local:HttpEndpoint"
/// section, started once the party is initialized and stopped before it (see <see cref="ApplicationIdentityStaticFeatureDriver"/>).
/// <para>
/// Each child of "Local:HttpEndpoint" is an endpoint, named after its key, with either "Urls" (separated by ';') or a
/// "Kestrel" section (Kestrel's own configuration: Endpoints, Certificates, Limits...). The application configures the
/// services and the pipeline of an endpoint with <see cref="HttpEndpointHostApplicationBuilderExtensions.AddHttpEndpoint"/>.
/// </para>
/// <para>
/// The other keys of an endpoint are its options (<see cref="HttpEndpointOption"/>, like the <see cref="ForwardedHeadersOption"/>):
/// any other key is an error.
/// </para>
/// <para>
/// This is a static feature: only the root party can have endpoints (a tenant domain that allows it is an error).
/// </para>
/// </summary>
public sealed class HttpEndpointFeatureDriver : ApplicationIdentityStaticFeatureDriver
{
    /// <summary>
    /// The configuration key of the feature, in the "Local" section.
    /// </summary>
    public const string ConfigurationKey = "HttpEndpoint";

    static readonly IConfiguration _emptyConfiguration = new ConfigurationBuilder().Build();

    readonly HttpEndpointHost _host;
    readonly IServiceProvider _services;
    readonly PartyComponentOptionDirectory _optionDirectory;
    readonly List<RunningHttpEndpoint> _endpoints;
    List<EndpointConfiguration>? _configurations;
    // The published endpoints: set once they are all started, emptied when they stop.
    RunningHttpEndpoint[] _published;
    bool _stopping;

    // DefaultEndpointUrl: the configured "Url" of the Kestrel endpoint named by "DefaultEndpoint".
    // Options: the enabled options of the endpoint, initialized, in their order.
    sealed record EndpointConfiguration( string Name,
                                         string[]? Urls,
                                         IConfigurationSection? Kestrel,
                                         string? DefaultEndpointUrl,
                                         IReadOnlyList<HttpEndpointOption> Options );

    /// <summary>
    /// Initializes a new <see cref="HttpEndpointFeatureDriver"/>.
    /// </summary>
    /// <param name="s">The application identity service.</param>
    /// <param name="host">The endpoints of the host.</param>
    /// <param name="services">The services of the host.</param>
    /// <param name="optionDirectory">The options of the party components (the options of the endpoints).</param>
    public HttpEndpointFeatureDriver( ApplicationIdentityService s,
                                      HttpEndpointHost host,
                                      IServiceProvider services,
                                      PartyComponentOptionDirectory optionDirectory )
        : base( s, isAllowedByDefault: false )
    {
        _host = host;
        _services = services;
        _optionDirectory = optionDirectory;
        _endpoints = new List<RunningHttpEndpoint>();
        _published = [];
    }

    /// <summary>
    /// Gets the running endpoints (empty until the host is started and after it is stopped).
    /// This is thread safe.
    /// </summary>
    public IReadOnlyList<RunningHttpEndpoint> Endpoints => _published;

    /// <summary>
    /// Gets whether this feature has nothing to start (it is not allowed or its setup failed) or its endpoints are
    /// started.
    /// </summary>
    internal bool IsInactiveOrStarted => _configurations == null || _published.Length > 0;

    /// <summary>
    /// Raised when the <see cref="Endpoints"/> are started and when they are stopped.
    /// </summary>
    internal event Action? EndpointsChanged;

    /// <inheritdoc />
    protected override Task<bool> SetupAsync( FeatureLifetimeContext context )
    {
        var monitor = context.Monitor;
        bool success = true;
        foreach( var local in context.GetAllLocals() )
        {
            if( local != ApplicationIdentityService && IsAllowedFeature( local ) )
            {
                monitor.Error( $"'{local}' allows the '{FeatureName}' feature: only the root party can have HTTP endpoints." );
                success = false;
            }
        }
        if( !IsRootAllowed )
        {
            if( _host.Configurations.Count > 0 )
            {
                monitor.Warn( $"The '{FeatureName}' feature is not allowed (\"Local:AllowFeatures\"): the endpoints configured by the application are ignored ({_host.Configurations.Keys.Concatenate()})." );
            }
            return Task.FromResult( success );
        }
        if( _host.HostServices == null )
        {
            monitor.Error( $"The services of the host are not available: the map doesn't contain '{typeof( HttpEndpointHostRegistration ):C}'." );
            return Task.FromResult( false );
        }
        var configurations = ReadConfigurations( monitor );
        if( configurations == null || !CheckAspNetDefaults( monitor ) ) return Task.FromResult( false );
        _configurations = configurations;
        return Task.FromResult( success );
    }

    List<EndpointConfiguration>? ReadConfigurations( IActivityMonitor monitor )
    {
        var section = ApplicationIdentityService.LocalConfiguration.Configuration.TryGetSection( ConfigurationKey );
        var children = section?.GetChildren();
        if( children == null || children.Count == 0 )
        {
            monitor.Error( $"The '{FeatureName}' feature is allowed but \"Local:{ConfigurationKey}\" defines no endpoint." );
            return null;
        }
        var result = new List<EndpointConfiguration>();
        bool success = true;
        foreach( var c in children )
        {
            var urls = ReadUrls( c );
            var kestrel = c.TryGetSection( "Kestrel" );
            if( (urls == null) == (kestrel == null) )
            {
                monitor.Error( $"The endpoint \"Local:{ConfigurationKey}:{c.Key}\" must have either \"Urls\" or a \"Kestrel\" section." );
                success = false;
            }
            else if( TryReadDefaultEndpoint( monitor, c, urls, kestrel, out var defaultEndpointUrl )
                     && TryCreateOptions( monitor, c, out var options ) )
            {
                result.Add( new EndpointConfiguration( c.Key, urls, kestrel, defaultEndpointUrl, options ) );
            }
            else
            {
                success = false;
            }
        }
        foreach( var name in _host.Configurations.Keys )
        {
            if( !result.Any( c => StringComparer.OrdinalIgnoreCase.Equals( c.Name, name ) ) )
            {
                monitor.Error( $"The application configures the endpoint '{name}' but \"Local:{ConfigurationKey}\" doesn't define it." );
                success = false;
            }
        }
        return success ? result : null;

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
            var path = $"Local:{ConfigurationKey}:{c.Key}";
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

    // The options enabled by the endpoint section, created from the host services and initialized. A key that is neither an
    // endpoint key nor an option is an error.
    bool TryCreateOptions( IActivityMonitor monitor, ImmutableConfigurationSection endpoint, out IReadOnlyList<HttpEndpointOption> options )
    {
        options = [];
        var ownerType = typeof( HttpEndpointFeatureDriver );
        bool success = true;
        foreach( var k in endpoint.GetChildren() )
        {
            if( k.Key is not ("Urls" or "Kestrel" or "DefaultEndpoint") && !_optionDirectory.IsOptionName( ownerType, k.Key ) )
            {
                var names = _optionDirectory.GetOptions( ownerType ).Select( o => o.Name ).Concatenate();
                monitor.Error( $"Unknown \"{k.Path}\": the keys of an endpoint are Urls, Kestrel, DefaultEndpoint and its options ({(names.Length > 0 ? names : "none")})." );
                success = false;
            }
        }
        if( !success || !_optionDirectory.TrySelect( monitor, ownerType, endpoint, out var selected ) ) return false;
        var result = new List<HttpEndpointOption>( selected.Count );
        foreach( var (info, configuration) in selected )
        {
            if( info.Create( _services ) is not HttpEndpointOption o )
            {
                monitor.Error( $"The option '{info.OptionType:C}' of the '{FeatureName}' feature must specialize '{typeof( HttpEndpointOption ):C}'." );
                success = false;
            }
            else if( o.Initialize( monitor, endpoint, configuration ) )
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

    // The endpoints are configured in "Local:HttpEndpoint" only: two places must not configure the same port.
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
        var message = $"The '{FeatureName}' feature configures the endpoints in \"Local:{ConfigurationKey}\": {found.Concatenate( " and " )} must be removed (they are ignored).";
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
        if( _configurations == null ) return;
        try
        {
            foreach( var c in _configurations )
            {
                var endpoint = await StartEndpointAsync( monitor, c, cancel ).ConfigureAwait( false );
                _endpoints.Add( endpoint );
                monitor.Info( $"{endpoint} started." );
            }
            _published = _endpoints.ToArray();
            EndpointsChanged?.Invoke();
        }
        catch
        {
            // The base doesn't call OnHostStoppingAsync when OnHostStartedAsync fails.
            await StopEndpointsAsync( monitor, default ).ConfigureAwait( false );
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
        if( _host.Configurations.TryGetValue( c.Name, out var configure ) ) configure?.Invoke( builder );
        // The middlewares of the options are the first ones, in their order: the last prepended action runs first.
        if( c.Options.Count > 0 )
        {
            builder.PrependApplicationBuilder( app =>
            {
                foreach( var o in c.Options ) o.ConfigurePipeline( app );
            }, beforeCKMiddleware: true );
        }
        var app = builder.CKBuild();
        // A party lives and dies with its endpoints: a web application that stops by itself stops the host.
        app.Lifetime.ApplicationStopping.Register( () => OnApplicationStopping( c.Name ) );
        await app.StartAsync( cancel ).ConfigureAwait( false );
        return new RunningHttpEndpoint( c.Name, app, configuredByUrls: c.Urls != null, c.DefaultEndpointUrl );
    }

    void OnApplicationStopping( string name )
    {
        if( _stopping ) return;
        var lifetime = _services.GetService<IHostApplicationLifetime>();
        if( lifetime != null && !lifetime.ApplicationStopping.IsCancellationRequested )
        {
            ActivityMonitor.StaticLogger.Warn( $"The web application of the endpoint '{name}' stops: the host is stopped." );
            lifetime.StopApplication();
        }
    }

    /// <summary>
    /// Raises the host's <see cref="IHostApplicationLifetime.ApplicationStopping"/>, then stops the endpoints.
    /// <para>
    /// The host raises it after the <see cref="IHostedLifecycleService.StoppingAsync"/> of its services, where the static
    /// features stop: without this, what aborts its long running requests on <c>ApplicationStopping</c> (the WebSocket
    /// channel for instance) would do it after the endpoints have drained them for their whole shutdown timeout. A web
    /// application raises it before its server drains: the endpoints keep this order.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>The awaitable.</returns>
    protected override Task OnHostStoppingAsync( IActivityMonitor monitor, CancellationToken cancel )
    {
        _services.GetService<IHostApplicationLifetime>()?.StopApplication();
        return StopEndpointsAsync( monitor, cancel );
    }

    // The web applications are stopped, never disposed: their containers would dispose the singletons forwarded to the host
    // (a container disposes what its factories return). Their lifetime is the host's.
    async Task StopEndpointsAsync( IActivityMonitor monitor, CancellationToken cancel )
    {
        _stopping = true;
        if( _published.Length > 0 )
        {
            _published = [];
            EndpointsChanged?.Invoke();
        }
        for( int i = _endpoints.Count - 1; i >= 0; --i )
        {
            var e = _endpoints[i];
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
        _endpoints.Clear();
    }

    /// <inheritdoc />
    protected override async Task TeardownAsync( FeatureLifetimeContext context )
    {
        if( _endpoints.Count > 0 )
        {
            context.Monitor.Warn( $"The endpoints are still running at the teardown (OnHostStoppingAsync has not been called): they are stopped now." );
            await StopEndpointsAsync( context.Monitor, default ).ConfigureAwait( false );
        }
    }

    sealed class NoHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync( CancellationToken cancellationToken ) => Task.CompletedTask;

        public Task StopAsync( CancellationToken cancellationToken ) => Task.CompletedTask;
    }
}
