using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AspNet.WebSocket.Tests;

/// <summary>
/// The dispatcher callbacks around the message loop: OnDisconnectedAsync (and the exception it carries)
/// and OnParsingIssueAsync. The echo tests already cover DispatchMessageAsync and the happy connect path.
/// </summary>
[TestFixture]
public class DispatcherCallbackTests
{
    [Test]
    public async Task on_disconnected_is_called_with_a_null_exception_on_a_graceful_close_Async()
    {
        var dispatcher = new EchoServer.EchoDispatcher();
        var (app, baseUri) = await EchoServer.StartAsync( dispatcher, () => new ActivityMonitor() );
        await using var _ = app;

        using( var client = new ClientWebSocket() )
        {
            await client.ConnectAsync( EchoServer.WsUri( baseUri ), CancellationToken.None );
            // A normal exchange, then the client closes with a proper handshake.
            (await EchoServer.EchoAsync( client, "Hello" )).ShouldBe( "Hello" );
            await client.CloseAsync( WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None );
        }

        await dispatcher.Disconnected.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
        dispatcher.DisconnectException.ShouldBeNull( "A graceful close carries no exception." );
    }

    [Test]
    public async Task on_disconnected_carries_the_exception_on_an_abnormal_close_Async()
    {
        var dispatcher = new EchoServer.EchoDispatcher();
        var (app, baseUri) = await EchoServer.StartAsync( dispatcher, () => new ActivityMonitor() );
        await using var _ = app;

        using( var client = new ClientWebSocket() )
        {
            await client.ConnectAsync( EchoServer.WsUri( baseUri ), CancellationToken.None );
            // A message frame that is never ended: the server assembles a message it can never complete.
            await client.SendAsync( Encoding.UTF8.GetBytes( "partial" ), WebSocketMessageType.Text, endOfMessage: false, CancellationToken.None );
            // Close the output frame after it. Ordered on the one TCP stream, so the server reads the
            // partial, then the close, and finds a message left mid-read: no abort race.
            await client.CloseOutputAsync( WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None );
        }

        await dispatcher.Disconnected.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
        dispatcher.DisconnectException.ShouldBeOfType<InvalidDataException>(
            "A connection that ends mid-message surfaces the terminated-while-reading error." );
    }

    [Test]
    public async Task on_parsing_issue_is_called_and_the_connection_survives_Async()
    {
        var dispatcher = new EchoServer.EchoDispatcher();
        var (app, baseUri) = await EchoServer.StartAsync( dispatcher, () => new ActivityMonitor(), new ThrowingTextProtocol() );
        await using var _ = app;

        using( var client = new ClientWebSocket() )
        {
            await client.ConnectAsync( EchoServer.WsUri( baseUri ), CancellationToken.None );

            // The protocol throws on this payload: it lands on OnParsingIssueAsync, not OnDisconnected.
            await client.SendAsync( Encoding.UTF8.GetBytes( ThrowingTextProtocol.Poison ), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None );
            var issue = await dispatcher.ParsingIssue.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            issue.ShouldBeOfType<InvalidOperationException>();

            // A well-formed message right after proves the connection went through the parse error unharmed.
            (await EchoServer.EchoAsync( client, "after" )).ShouldBe( "after" );
            dispatcher.Disconnected.Task.IsCompleted.ShouldBeFalse( "A parse error must not disconnect the client." );
        }
    }
}
