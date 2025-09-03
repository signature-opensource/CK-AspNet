using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.Testing;

public static class AspNetServerTestHelperExtensions
{

    /// <summary>
    /// Creates a <see cref="RunningAspNetServer"/> from this configured WebApplication by calling <see cref="ApplicationBuilderCKAspNetExtensions.CKBuild(WebApplicationBuilder)"/>,
    /// starts the <see cref="WebApplication"/> on a random port and returns a <see cref="RunningAspNetServer"/>.
    /// <para>
    /// This throws if anything goes wrong.
    /// </para>
    /// </summary>
    /// <param name="builder">This web application.</param>
    /// <param name="map">Optional CKomposable map to register.</param>
    /// <param name="configureApplication">Optional application configurator.</param>
    /// <returns>A running .NET server.</returns>
    public static async Task<RunningAspNetServer> CreateRunningAspNetServerAsync( this WebApplicationBuilder builder,
                                                                                  IStObjMap? map = null,
                                                                                  Action<WebApplication>? configureApplication = null )
    {
        WebApplication? app = null;
        try
        {
            app = builder.CKBuild( map );

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
            return new RunningAspNetServer( app, serverAddress );
        }
        catch( Exception ex )
        {
            TestHelper.Monitor.Error( "Unhandled error while starting http server.", ex );
            if( app != null ) await app.DisposeAsync();
            throw;
        }
    }
}
