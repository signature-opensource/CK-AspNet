using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading.Tasks;

namespace CK.Testing;

/// <summary>
/// Running AspNet server with its <see cref="ServerAddress"/> (an url like "http://127.0.0.1:60346"),
/// <see cref="Services"/> and <see cref="Configuration"/> and a basic <see cref="Client"/>: a party (a <see cref="Host"/>
/// whose <see cref="Application"/> is the web application of its endpoint) or a standalone web application (see
/// <see cref="AspNetServerTestHelperExtensions"/>).
/// <para>
/// This class is sealed: it should be extended through extension methods. If state must be maintained, the best way is
/// to register a dedicated singleton service and use the <see cref="Services"/> from the extension methods to expose the
/// state. This approach has the advantage to make this state easily accessible from the running application services.
/// </para>
/// </summary>
public sealed partial class RunningAspNetServer : IAsyncDisposable
{
    readonly string _serverAddress;
    readonly IHost? _host;
    readonly WebApplication _app;
    RunningClient? _client;

    internal RunningAspNetServer( IHost? host, WebApplication app, string serverAddress )
    {
        _host = host;
        _app = app;
        _serverAddress = serverAddress;
    }

    /// <summary>
    /// Gets the host of a party. Null for a standalone web application.
    /// </summary>
    public IHost? Host => _host;

    /// <summary>
    /// Gets the web application: the one of the endpoint of a party, or the standalone one.
    /// </summary>
    public WebApplication Application => _app;

    /// <summary>
    /// Gets the services of the host of a party (the web application of its endpoint gets them), or the ones of the
    /// standalone web application.
    /// </summary>
    public IServiceProvider Services => _host?.Services ?? _app.Services;

    /// <summary>
    /// Gets the configuration of the host of a party, or the one of the standalone web application.
    /// </summary>
    public IConfiguration Configuration => _host?.Services.GetRequiredService<IConfiguration>() ?? _app.Configuration;

    /// <summary>
    /// Gets the server address (the port is automatically assigned).
    /// Example: "http://127.0.0.1:60346"
    /// </summary>
    public string ServerAddress => _serverAddress;

    /// <summary>
    /// Gets a simple client for this server.
    /// </summary>
    public RunningClient Client => _client ??= new RunningClient( this );

    /// <summary>
    /// Stops the application: the host of a party is stopped and disposed.
    /// </summary>
    /// <returns>The awaitable.</returns>
    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if( _host != null )
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        else
        {
            await _app.StopAsync();
        }
    }
}
