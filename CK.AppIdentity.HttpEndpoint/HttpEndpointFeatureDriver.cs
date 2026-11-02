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
    readonly List<RunningHttpEndpoint> _endpoints;
    List<EndpointConfiguration>? _configurations;
    bool _stopping;

    sealed record EndpointConfiguration( string Name, string[]? Urls, IConfigurationSection? Kestrel );

    /// <summary>
    /// Initializes a new <see cref="HttpEndpointFeatureDriver"/>.
    /// </summary>
    /// <param name="s">The application identity service.</param>
    /// <param name="host">The endpoints of the host.</param>
    /// <param name="services">The services of the host.</param>
    public HttpEndpointFeatureDriver( ApplicationIdentityService s, HttpEndpointHost host, IServiceProvider services )
        : base( s, isAllowedByDefault: false )
    {
        _host = host;
        _services = services;
        _endpoints = new List<RunningHttpEndpoint>();
    }

    /// <summary>
    /// Gets the running endpoints (empty until the host is started and after it is stopped).
    /// </summary>
    public IReadOnlyList<RunningHttpEndpoint> Endpoints => _endpoints;

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
            else
            {
                result.Add( new EndpointConfiguration( c.Key, urls, kestrel ) );
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

        static string[]? ReadUrls( ImmutableConfigurationSection c )
        {
            var value = c["Urls"];
            var urls = value != null
                        ? value.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries )
                        : c.TryGetSection( "Urls" )?.GetChildren().Select( u => u.Value ).Where( u => !string.IsNullOrWhiteSpace( u ) ).Select( u => u!.Trim() ).ToArray();
            return urls is { Length: > 0 } ? urls : null;
        }
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
        _services.GetService<WebApplicationContributions>()?.ApplyTo( builder );
        if( _host.Configurations.TryGetValue( c.Name, out var configure ) ) configure?.Invoke( builder );
        var app = builder.CKBuild();
        // A party lives and dies with its endpoints: a web application that stops by itself stops the host.
        app.Lifetime.ApplicationStopping.Register( () => OnApplicationStopping( c.Name ) );
        await app.StartAsync( cancel ).ConfigureAwait( false );
        return new RunningHttpEndpoint( c.Name, app );
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

    /// <inheritdoc />
    protected override Task OnHostStoppingAsync( IActivityMonitor monitor, CancellationToken cancel ) => StopEndpointsAsync( monitor, cancel );

    // The web applications are stopped, never disposed: their containers would dispose the singletons forwarded to the host
    // (a container disposes what its factories return). Their lifetime is the host's.
    async Task StopEndpointsAsync( IActivityMonitor monitor, CancellationToken cancel )
    {
        _stopping = true;
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
