using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CK.AspNet.WebSocket.Tests;

/// <summary>
/// A WebSocket host whose protocol, dispatcher, services and transport options a test chooses.
/// <see cref="EchoServer"/> covers the common delimited-text case; this covers the paths EchoServer
/// hard-codes away: a custom (non-delimited) protocol, a dispatcher activated through DI, and a
/// non-default <see cref="TransferFormat"/>. The message types are string in and string out.
/// </summary>
static class ConfigurableHost
{
    public const string Path = "/ws";

    /// <summary>Starts a host on a random free port.</summary>
    /// <param name="build">Configures the protocol and the dispatcher.</param>
    /// <param name="configureServices">Optional extra service registrations, run before the app is built.</param>
    /// <param name="configureOptions">Optional transport options (transfer format, timeouts, buffers).</param>
    public static async Task<(WebApplication App, Uri BaseUri)> StartAsync(
        Action<MessageDispatcherBuilder<string, string>> build,
        Action<IServiceCollection>? configureServices = null,
        Action<WebSocketConnectionDispatcherOptions>? configureOptions = null )
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddWebSocketServer();
        // The middleware reads the request scoped monitor, exactly as CKBuild registers one in a real app.
        builder.Services.AddScoped<IActivityMonitor>( _ => new ActivityMonitor() );
        configureServices?.Invoke( builder.Services );

        var app = builder.Build();
        app.Urls.Add( "http://127.0.0.1:0" ); // Random free port.
        app.UseWebSocketServer<string, string>( Path, build, configureOptions );
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (app, new Uri( address ));
    }

    /// <summary>Gets the ws:// address of the endpoint.</summary>
    public static Uri WsUri( Uri baseUri ) => new Uri( "ws://" + baseUri.Authority + Path );
}
