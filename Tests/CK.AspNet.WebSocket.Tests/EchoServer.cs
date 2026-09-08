using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using System.Net.WebSockets;
using System.Text;
using CK.AspNet.WebSocket;

namespace CK.AspNet.WebSocket.Tests;

/// <summary>
/// A minimal echo host on a random free port: the smallest thing that exercises the whole
/// CK.AspNet.WebSocket stack (middleware, transport, connection, handler, dispatcher) without any routing.
/// </summary>
static class EchoServer
{
    public const string Path = "/echo";

    /// <summary>
    /// Echoes every message back. Also records what the connection callbacks observed so tests can
    /// assert on them.
    /// </summary>
    public sealed class EchoDispatcher : IWebSocketMessageDispatcher<string, string>
    {
        /// <summary>Completed once <see cref="OnDisconnectedAsync"/> has been called.</summary>
        public TaskCompletionSource Disconnected { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );

        /// <summary>The monitor the connection exposed to <see cref="OnConnectedAsync"/>.</summary>
        public IActivityMonitor? ConnectionMonitor { get; private set; }

        public Task OnConnectedAsync( IActivityMonitor monitor, IWebSocketConnectionContext<string> connection )
        {
            return Task.CompletedTask;
        }

        public Task OnDisconnectedAsync( IActivityMonitor monitor, IWebSocketConnectionContext<string> connection, Exception? exception )
        {
            Disconnected.TrySetResult();
            return Task.CompletedTask;
        }

        public Task DispatchMessageAsync( IActivityMonitor monitor, IWebSocketConnectionContext<string> connection, string message )
            => connection.WriteAsync( message ).AsTask();

        public Task OnParsingIssueAsync( IActivityMonitor monitor, IWebSocketConnectionContext<string> connection, Exception exception )
            => Task.CompletedTask;
    }

    /// <summary>
    /// Starts an echo host with a fresh, unfiltered <see cref="ActivityMonitor"/> registered as the
    /// request scoped monitor, exactly what CKBuild does in a real application.
    /// </summary>
    public static Task<(WebApplication App, Uri BaseUri)> StartAsync()
        => StartAsync( new EchoDispatcher(), () => new ActivityMonitor() );

    /// <summary>
    /// Starts an echo host.
    /// </summary>
    /// <param name="dispatcher">The dispatcher to use.</param>
    /// <param name="requestMonitor">
    /// Factory of the request scoped <see cref="IActivityMonitor"/>, called once per request scope.
    /// Null registers nothing: the host then has no request monitor at all.
    /// </param>
    public static async Task<(WebApplication App, Uri BaseUri)> StartAsync( IWebSocketMessageDispatcher<string, string> dispatcher, Func<IActivityMonitor>? requestMonitor )
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddWebSocketServer();
        if( requestMonitor != null )
        {
            builder.Services.AddScoped( _ => requestMonitor() );
        }
        var app = builder.Build();
        app.Urls.Add( "http://127.0.0.1:0" ); // Random free port.
        // The whole point of CK.AspNet.WebSocket: no UseRouting, no UseEndpoints, anywhere.
        app.UseWebSocketServer<string, string>( Path, b =>
        {
            b.UseEndOfMessageDelimitedProtocol( new TextProtocol() );
            b.UseDispatcher( dispatcher );
        } );
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (app, new Uri( address ));
    }

    /// <summary>Gets the ws:// address of the echo endpoint.</summary>
    public static Uri WsUri( Uri baseUri ) => new Uri( "ws://" + baseUri.Authority + Path );

    /// <summary>Sends a text message and returns what the server echoed back.</summary>
    public static async Task<string> EchoAsync( ClientWebSocket client, string message )
    {
        var payload = Encoding.UTF8.GetBytes( message );
        await client.SendAsync( payload, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None );

        using var cts = new CancellationTokenSource( TimeSpan.FromSeconds( 5 ) );
        var buffer = new byte[1024];
        var received = await client.ReceiveAsync( buffer, cts.Token );
        received.EndOfMessage.ShouldBeTrue();
        return Encoding.UTF8.GetString( buffer, 0, received.Count );
    }
}
