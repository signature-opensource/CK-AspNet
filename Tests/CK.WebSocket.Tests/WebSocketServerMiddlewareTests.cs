using NUnit.Framework;
using Shouldly;
using System.Net.WebSockets;

namespace CK.WebSocket.Tests;

[TestFixture]
public class WebSocketServerMiddlewareTests
{
    [Test]
    public async Task echo_works_without_any_endpoint_routing_Async()
    {
        var (app, baseUri) = await EchoServer.StartAsync();
        await using var _ = app;

        using var client = new ClientWebSocket();
        await client.ConnectAsync( EchoServer.WsUri( baseUri ), CancellationToken.None );
        (await EchoServer.EchoAsync( client, "Hello" )).ShouldBe( "Hello" );
    }

    [Test]
    public async Task non_websocket_requests_get_a_400_on_the_path_and_flow_past_it_elsewhere_Async()
    {
        var (app, baseUri) = await EchoServer.StartAsync();
        await using var _ = app;

        using var http = new HttpClient();
        var onPath = await http.GetAsync( new Uri( baseUri, EchoServer.Path ) );
        ((int)onPath.StatusCode).ShouldBe( 400, "A plain GET on the WebSocket path must be answered by the middleware." );

        var elsewhere = await http.GetAsync( new Uri( baseUri, "/other" ) );
        ((int)elsewhere.StatusCode).ShouldBe( 404, "Other paths must flow through to the rest of the pipeline." );
    }
}
