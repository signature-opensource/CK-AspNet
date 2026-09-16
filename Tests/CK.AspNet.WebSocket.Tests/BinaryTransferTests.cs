using NUnit.Framework;
using Shouldly;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AspNet.WebSocket.Tests;

/// <summary>
/// The transport format and large messages, end to end. TransferFormat drives whether the server sends
/// Text or Binary WebSocket frames; receiving is format-agnostic. A message larger than the transport
/// buffer must reassemble across several frames both ways.
/// </summary>
[TestFixture]
public class BinaryTransferTests
{
    [Test]
    public async Task the_server_sends_binary_frames_when_TransferFormat_is_Binary_Async()
    {
        var (app, baseUri) = await ConfigurableHost.StartAsync(
            build: b =>
            {
                b.UseEndOfMessageDelimitedProtocol( new TextProtocol() );
                b.UseDispatcher( new EchoServer.EchoDispatcher() );
            },
            configureOptions: o => o.WebSockets.TransferFormat = TransferFormat.Binary );
        await using var _ = app;

        using var client = new ClientWebSocket();
        await client.ConnectAsync( ConfigurableHost.WsUri( baseUri ), CancellationToken.None );

        // The client may send in any format; the server honours its configured one on the way out.
        await client.SendAsync( Encoding.UTF8.GetBytes( "ping" ), WebSocketMessageType.Text, true, CancellationToken.None );

        using var cts = new CancellationTokenSource( TimeSpan.FromSeconds( 5 ) );
        var buffer = new byte[1024];
        var received = await client.ReceiveAsync( buffer, cts.Token );
        received.MessageType.ShouldBe( WebSocketMessageType.Binary, "TransferFormat.Binary must make the server send binary frames." );
        Encoding.UTF8.GetString( buffer, 0, received.Count ).ShouldBe( "ping" );
    }

    [Test]
    public async Task a_large_message_round_trips_across_frames_Async()
    {
        var (app, baseUri) = await ConfigurableHost.StartAsync( b =>
        {
            b.UseEndOfMessageDelimitedProtocol( new TextProtocol() );
            b.UseDispatcher( new EchoServer.EchoDispatcher() );
        } );
        await using var _ = app;

        using var client = new ClientWebSocket();
        await client.ConnectAsync( ConfigurableHost.WsUri( baseUri ), CancellationToken.None );

        // Well past the 64KB transport buffer, so the transport must split and reassemble both ways.
        var big = new string( 'x', 256 * 1024 );
        await client.SendAsync( Encoding.UTF8.GetBytes( big ), WebSocketMessageType.Text, true, CancellationToken.None );

        (await ReceiveWholeTextAsync( client )).ShouldBe( big );
    }

    static async Task<string> ReceiveWholeTextAsync( ClientWebSocket client )
    {
        using var cts = new CancellationTokenSource( TimeSpan.FromSeconds( 10 ) );
        var buffer = new byte[8192];
        var text = new StringBuilder();
        WebSocketReceiveResult received;
        do
        {
            received = await client.ReceiveAsync( buffer, cts.Token );
            text.Append( Encoding.UTF8.GetString( buffer, 0, received.Count ) );
        }
        while( !received.EndOfMessage );
        return text.ToString();
    }
}
