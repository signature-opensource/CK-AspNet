using CK.AppIdentity;
using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.Testing;

/// <summary>
/// Creates a <see cref="RunningAspNetServer"/>: a party (a Generic Host with its "HttpEndpoint" feature) from a map, or a
/// standalone web application for a test of a middleware.
/// </summary>
public static class AspNetServerTestHelperExtensions
{

    /// <summary>
    /// Creates and starts a party from this map, as <c>CK.Core.CKomposableAppHost</c> (CK.AppHost) does: a Generic Host
    /// whose web application is the endpoint of its "HttpEndpoint" feature, on a random port.
    /// The endpoint gets the contributions of the packages (<c>WebApplicationContributions</c>), the services of the host,
    /// then <paramref name="configureEndpoint"/>; the public URL of the party is the endpoint's one.
    /// <para>
    /// The map must contain the "HttpEndpoint" feature (the CK.AppIdentity.HttpEndpoint assembly, see
    /// <see cref="HttpEndpointAssembly"/>). The identity is "Test/$Server" by default; whatever <paramref name="identity"/>
    /// does, the "HttpEndpoint" feature is allowed and the endpoint listens on
    /// "http://127.0.0.1:0" unless it is configured.
    /// </para>
    /// <para>
    /// This throws if anything goes wrong. Disposing the server stops and disposes the host.
    /// </para>
    /// </summary>
    /// <param name="map">The StObjMap of the party.</param>
    /// <param name="identity">Optional configuration of the "CK-AppIdentity" section.</param>
    /// <param name="configureHost">Optional configuration of the host (its services, its environment...), before the map is registered.</param>
    /// <param name="configureEndpoint">Optional configuration of the web application of the endpoint (its routes with <c>AppendApplicationBuilder</c>...).</param>
    /// <returns>A running party.</returns>
    public static async Task<RunningAspNetServer> CreateRunningAspNetServerAsync( this IStObjMap map,
                                                                                  Action<MutableConfigurationSection>? identity = null,
                                                                                  Action<HostApplicationBuilder>? configureHost = null,
                                                                                  Action<WebApplicationBuilder>? configureEndpoint = null )
    {
        Throw.CheckNotNullArgument( map );
        IHost? host = null;
        try
        {
            var builder = Host.CreateApplicationBuilder( new HostApplicationBuilderSettings
            {
                DisableDefaults = true,
                EnvironmentName = Environments.Production,
                ContentRootPath = TestHelper.TestProjectFolder
            } );
            var configuration = ApplicationIdentityServiceConfiguration.Create( TestHelper.Monitor, c =>
            {
                c["FullName"] = "Test/$Server";
                identity?.Invoke( c );
                var allowed = c["Local:AllowFeatures"];
                if( string.IsNullOrWhiteSpace( allowed ) ) c["Local:AllowFeatures"] = "HttpEndpoint";
                else if( !allowed.Split( ',' ).Any( f => f.Trim() == "HttpEndpoint" ) ) c["Local:AllowFeatures"] = allowed + ",HttpEndpoint";
                // A configured endpoint (its "Urls" or its "Kestrel" section) is kept.
                if( Find( c, "Local", "HttpEndpoint", "Urls" ) == null && Find( c, "Local", "HttpEndpoint", "Kestrel" ) == null )
                {
                    c["Local:HttpEndpoint:Urls"] = "http://127.0.0.1:0";
                }
            } );
            Throw.CheckState( "Invalid identity configuration.", configuration != null );
            builder.Services.AddSingleton( configuration );
            // The monitor of the work outside the requests (the requests have the one of their ScopedHttpContext).
            builder.Services.AddScoped<IActivityMonitor, ActivityMonitor>();
            builder.Services.AddScoped( sp => sp.GetRequiredService<IActivityMonitor>().ParallelLogger );
            configureHost?.Invoke( builder );
            builder.Services.AddStObjMap( TestHelper.Monitor, map );
            if( configureEndpoint != null ) builder.ConfigureHttpEndpoint( configureEndpoint );
            host = builder.Build();
            await host.StartAsync().ConfigureAwait( false );

            var driver = host.Services.GetService<HttpEndpointFeatureDriver>();
            Throw.CheckState( "The map doesn't contain the \"HttpEndpoint\" feature (CK.AppIdentity.HttpEndpoint).", driver != null );
            var endpoint = driver.Endpoint;
            Throw.CheckState( "The endpoint is not running: see the logs of the \"HttpEndpoint\" feature.", endpoint != null );
            var serverAddress = endpoint.Urls[0];
            TestHelper.Monitor.Info( $"Party started. Endpoint address: '{serverAddress}'." );
            return new RunningAspNetServer( host, endpoint.Application, serverAddress );
        }
        catch( Exception ex )
        {
            TestHelper.Monitor.Error( "Unhandled error while starting the party.", ex );
            if( host != null )
            {
                await host.StopAsync().ConfigureAwait( false );
                host.Dispose();
            }
            throw;
        }
    }

    // Finds a section without creating it (GetMutableSection creates the missing ones).
    static MutableConfigurationSection? Find( MutableConfigurationSection s, params string[] keys )
    {
        MutableConfigurationSection? found = s;
        foreach( var k in keys )
        {
            found = found.GetMutableChildren().FirstOrDefault( c => string.Equals( c.Key, k, StringComparison.OrdinalIgnoreCase ) );
            if( found == null ) return null;
        }
        return found;
    }

    /// <summary>
    /// The assembly of the "HttpEndpoint" feature: the BinPath of a party created by
    /// <see cref="CreateRunningAspNetServerAsync(IStObjMap, Action{MutableConfigurationSection}?, Action{HostApplicationBuilder}?, Action{WebApplicationBuilder}?)"/>
    /// contains it.
    /// </summary>
    public const string HttpEndpointAssembly = "CK.AppIdentity.HttpEndpoint";

    /// <summary>
    /// Creates a standalone web application from this builder: <see cref="ApplicationBuilderCKAspNetExtensions.CKBuild(WebApplicationBuilder)"/>
    /// is called, the application is started on a random port. This is for a test of a middleware or of the pipeline
    /// (CK.AspNet): a party is created from its map, see
    /// <see cref="CreateRunningAspNetServerAsync(IStObjMap, Action{MutableConfigurationSection}?, Action{HostApplicationBuilder}?, Action{WebApplicationBuilder}?)"/>.
    /// <para>
    /// This throws if anything goes wrong.
    /// </para>
    /// </summary>
    /// <param name="builder">This web application builder.</param>
    /// <param name="configureApplication">Optional application configurator.</param>
    /// <returns>A running .NET server.</returns>
    public static async Task<RunningAspNetServer> CreateRunningAspNetServerAsync( this WebApplicationBuilder builder,
                                                                                  Action<WebApplication>? configureApplication = null )
    {
        WebApplication? app = null;
        try
        {
            app = builder.CKBuild();

            // This chooses a random, free port.
            app.Urls.Add( "http://127.0.0.1:0" );

            configureApplication?.Invoke( app );
            await app.StartAsync().ConfigureAwait( false );

            // The IServer's IServerAddressesFeature feature has the address resolved.
            var server = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
            var addresses = server.Features.Get<IServerAddressesFeature>();
            Throw.DebugAssert( addresses != null && addresses.Addresses.Count > 0 );

            var serverAddress = addresses.Addresses.First();
            TestHelper.Monitor.Info( $"Server started. Server address: '{serverAddress}'." );
            return new RunningAspNetServer( null, app, serverAddress );
        }
        catch( Exception ex )
        {
            TestHelper.Monitor.Error( "Unhandled error while starting http server.", ex );
            if( app != null ) await app.DisposeAsync();
            throw;
        }
    }
}
