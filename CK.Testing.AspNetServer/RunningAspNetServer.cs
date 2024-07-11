using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;

namespace CK.Testing
{
    /// <summary>
    /// Running AspNet server with its <see cref="ServerAddress"/> (an url like "http://[::1]:60346"),
    /// <see cref="Services"/> and <see cref="Configuration"/> and a basic <see cref="Client"/>.
    /// <para>
    /// The <see cref="AspNetServerTestHelperExtensions.CreateMinimalAspNetServerAsync(IMonitorTestHelper, Action{Microsoft.Extensions.DependencyInjection.IServiceCollection}?, Action{IApplicationBuilder}?)"/>
    /// is not "generic", this class is sealed.
    /// It should be extended through extension methods. If state must be maintained, the best way is to register a
    /// dedicated singleton service and use the <see cref="Services"/> from the extension methods to expose the state.
    /// This approach has the advantage to make this state easily accessible from the running application services.
    /// </para>
    /// </summary>
    public sealed partial class RunningAspNetServer : IAsyncDisposable
    {
        readonly string _serverAddress;
        readonly WebApplication _app;
        RunningClient? _client;


        internal RunningAspNetServer( WebApplication app, string serverAddress )
        {
            _app = app;
            _serverAddress = serverAddress;
        }

        /// <summary>
        /// Gets the application's configured services.
        /// </summary>
        public IServiceProvider Services => _app.Services;

        /// <summary>
        /// Gets the application's configuration.
        /// </summary>
        public IConfiguration Configuration => _app.Configuration;

        /// <summary>
        /// Gets the server address (the port is automatically assigned).
        /// Example: "http://[::1]:60346"
        /// </summary>
        public string ServerAddress => _serverAddress;

        /// <summary>
        /// Gets a simple client for this server.
        /// </summary>
        public RunningClient Client => _client ??= new RunningClient( this );

        /// <summary>
        /// Stops the application.
        /// </summary>
        /// <returns>The awaitable.</returns>
        public async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            await _app.StopAsync();
        }
    }

}
