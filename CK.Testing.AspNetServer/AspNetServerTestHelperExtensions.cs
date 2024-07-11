using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.Testing
{
    public static class AspNetServerTestHelperExtensions
    {
        /// <summary>
        /// Creates, configures and starts a <see cref="RunningAspNetServer"/> (that must be disposed once done with it).
        /// <para>
        /// This services configuration is minimal:
        /// <code>
        ///             builder.AddScopedHttpContext();
        ///             configureServices?.Invoke( builder.Services );
        ///             // Register the IActivityMonitor and its ParallelLogger (if they are not already registered).
        ///             builder.Services.TryAddScoped&lt;IActivityMonitor, ActivityMonitor&gt;();
        ///             builder.Services.TryAddScoped( sp => sp.GetRequiredService&lt;IActivityMonitor&gt;().ParallelLogger );
        /// </code>
        /// As well as the the pipeline (done by <see cref="BuildAndCreateRunningAspNetServerAsync(WebApplicationBuilder, Action{IApplicationBuilder}?)"/>):
        /// <code>
        ///             app.UseScopedHttpContext();
        ///             app.UseGuardRequestMonitor();
        ///             // This chooses a random, free port.
        ///             app.Urls.Add( "http://[::1]:0" );
        ///             configureApplication?.Invoke( app );
        /// </code>
        /// </para>
        /// </summary>
        /// <param name="helper">This helper.</param>
        /// <param name="configureServices">Application services configurator.</param>
        /// <param name="configureApplication">Optional application configurator.</param>
        /// <returns>A running .NET server or null if an error occurred or the server failed to start.</returns>
        public static async Task<RunningAspNetServer> CreateMinimalAspNetServerAsync( this IMonitorTestHelper helper,
                                                                                      Action<IServiceCollection>? configureServices,
                                                                                      Action<IApplicationBuilder>? configureApplication = null )
        {
            var builder = WebApplication.CreateBuilder();
            builder.AddScopedHttpContext();
            configureServices?.Invoke( builder.Services );
            // Don't UseCKMonitoring() here or the GrandOutput.Default will be reconfigured:
            // only register the IActivityMonitor and its ParallelLogger (if they are not already registered).
            builder.Services.TryAddScoped<IActivityMonitor, ActivityMonitor>();
            builder.Services.TryAddScoped( sp => sp.GetRequiredService<IActivityMonitor>().ParallelLogger );

            return await builder.BuildAndCreateRunningAspNetServerAsync( configureApplication ).ConfigureAwait( false );
        }

        /// <summary>
        /// Creates a <see cref="RunningAspNetServer"/> from this configured WebApplication with a minimal pipeline:
        /// <code>
        ///             app.UseScopedHttpContext();
        ///             app.UseGuardRequestMonitor();
        ///             // This chooses a random, free port.
        ///             app.Urls.Add( "http://[::1]:0" );
        ///             configureApplication?.Invoke( app );
        /// </code>
        /// </summary>
        /// <param name="appBuilder">This web application.</param>
        /// <param name="configureApplication">Optional application configurator.</param>
        /// <returns>A running .NET server or null if an error occurred or the server failed to start.</returns>
        public static async Task<RunningAspNetServer> BuildAndCreateRunningAspNetServerAsync( this WebApplicationBuilder appBuilder,
                                                                                              Action<IApplicationBuilder>? configureApplication )
        {
            WebApplication? app = null;
            try
            {
                app = appBuilder.Build();
                app.UseScopedHttpContext();
                app.UseGuardRequestMonitor();

                // This chooses a random, free port.
                app.Urls.Add( "http://[::1]:0" );

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

}
