using CK.WebSocket;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System.Net.WebSockets;
using System.Text;

namespace CK.WebSocket.Tests;

[TestFixture]
public class WebSocketServerMiddlewareTests
{
    sealed class EchoDispatcher : IWebSocketMessageDispatcher<string, string>
    {
        public Task OnConnectedAsync( IWebSocketConnectionContext<string> connection ) => Task.CompletedTask;

        public Task OnDisconnectedAsync( IWebSocketConnectionContext<string> connection, Exception? exception ) => Task.CompletedTask;

        public Task DispatchMessageAsync( IWebSocketConnectionContext<string> connection, string message )
            => connection.WriteAsync( message ).AsTask();
    }

    static async Task<(WebApplication App, Uri BaseUri)> StartEchoAppAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddWebSocketServer();
        var app = builder.Build();
        app.Urls.Add( "http://127.0.0.1:0" ); // Random free port.
        // The whole point of CK.WebSocket: no UseRouting, no UseEndpoints, anywhere.
        app.UseWebSocketServer<string, string>( "/echo", b =>
        {
            b.UseEndOfMessageDelimitedProtocol( new TextProtocol() );
            b.UseDispatcher( new EchoDispatcher() );
        } );
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (app, new Uri( address ));
    }

    [Test]
    public async Task echo_works_without_any_endpoint_routing_Async()
    {
        var (app, baseUri) = await StartEchoAppAsync();
        await using var _ = app;

        using var client = new ClientWebSocket();
        await client.ConnectAsync( new Uri( "ws://" + baseUri.Authority + "/echo" ), CancellationToken.None );
        var payload = Encoding.UTF8.GetBytes( "Hello" );
        await client.SendAsync( payload, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None );

        using var cts = new CancellationTokenSource( TimeSpan.FromSeconds( 5 ) );
        var buffer = new byte[1024];
        var received = await client.ReceiveAsync( buffer, cts.Token );
        received.EndOfMessage.ShouldBeTrue();
        Encoding.UTF8.GetString( buffer, 0, received.Count ).ShouldBe( "Hello" );
    }

    [Test]
    public async Task non_websocket_requests_get_a_400_on_the_path_and_flow_past_it_elsewhere_Async()
    {
        var (app, baseUri) = await StartEchoAppAsync();
        await using var _ = app;

        using var http = new HttpClient();
        var onPath = await http.GetAsync( new Uri( baseUri, "/echo" ) );
        ((int)onPath.StatusCode).ShouldBe( 400, "A plain GET on the WebSocket path must be answered by the middleware." );

        var elsewhere = await http.GetAsync( new Uri( baseUri, "/other" ) );
        ((int)elsewhere.StatusCode).ShouldBe( 404, "Other paths must flow through to the rest of the pipeline." );
    }
}
