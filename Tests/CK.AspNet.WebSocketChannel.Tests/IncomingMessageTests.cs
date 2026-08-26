using CK.Core;
using CK.PerfectEvent;
using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Shouldly;

namespace CK.AspNet.WebSocketChannel.Tests;

/// <summary>
/// The ascending direction: it exists only for whoever subscribes to
/// <see cref="WebSocketChannelManager.MessageReceived"/>, and it must never let a client harm the
/// socket that the other features are using.
/// </summary>
[TestFixture]
public class IncomingMessageTests
{
    static Task SendAsync( ClientWebSocket client, string topic, string message )
    {
        var frame = Encoding.UTF8.GetBytes( $"{{\"topic\":\"{topic}\",\"message\":{message}}}" );
        return client.SendAsync( frame, WebSocketMessageType.Text, true, CancellationToken.None );
    }

    [Test]
    public async Task an_incoming_message_is_raised_with_its_topic_and_payload_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        await using var host = await WebSocketChannelHost.StartAsync( map );

        var received = new TaskCompletionSource<(string Topic, string Message, string ConnectionId)>(
                            TaskCreationOptions.RunContinuationsAsynchronously );
        AsyncSequentialEventHandler<MessageReceivedEvent> onMessage = ( monitor, e, cancel ) =>
        {
            // The payload is a copy: reading it after an await is exactly what a real handler does.
            var text = Encoding.UTF8.GetString( e.Message.Span );
            received.TrySetResult( (e.Topic, text, e.Connection.ConnectionId) );
            return Task.CompletedTask;
        };
        host.Manager.MessageReceived.Async += onMessage;
        try
        {
            var (client, connectionId) = await host.ConnectAsync();
            using( client )
            {
                await SendAsync( client, "OD", """{"watch":"D"}""" );
                var r = await received.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                r.Topic.ShouldBe( "OD" );
                r.Message.ShouldBe( """{"watch":"D"}""", "The payload is handed over as the client wrote it." );
                r.ConnectionId.ShouldBe( connectionId, "A handler knows which connection spoke, so it can answer it." );
            }
        }
        finally
        {
            host.Manager.MessageReceived.Async -= onMessage;
        }
    }

    [Test]
    public async Task a_feature_sees_the_other_topics_and_ignores_them_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        await using var host = await WebSocketChannelHost.StartAsync( map );

        // One socket, several features: routing is the subscriber's job, so this checks that the topic
        // it needs to filter on is actually there and correct.
        var topics = new ConcurrentQueue<string>();
        var twoSeen = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        SequentialEventHandler<MessageReceivedEvent> onMessage = ( monitor, e ) =>
        {
            topics.Enqueue( e.Topic );
            if( topics.Count == 2 ) twoSeen.TrySetResult();
        };
        host.Manager.MessageReceived.Sync += onMessage;
        try
        {
            var (client, _) = await host.ConnectAsync();
            using( client )
            {
                await SendAsync( client, "OD", "1" );
                await SendAsync( client, "SC", "2" );
                await twoSeen.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                topics.ShouldBe( ["OD", "SC"] );
            }
        }
        finally
        {
            host.Manager.MessageReceived.Sync -= onMessage;
        }
    }

    [Test]
    public async Task a_malformed_message_is_dropped_and_the_connection_survives_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        await using var host = await WebSocketChannelHost.StartAsync( map );

        var good = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        SequentialEventHandler<MessageReceivedEvent> onMessage = ( monitor, e ) =>
        {
            if( e.Topic == "OD" ) good.TrySetResult();
        };
        host.Manager.MessageReceived.Sync += onMessage;
        try
        {
            var (client, connectionId) = await host.ConnectAsync();
            using( client )
            {
                // A client can send anything, and a shared socket must not die of it.
                await client.SendAsync( Encoding.UTF8.GetBytes( "not json at all" ), WebSocketMessageType.Text, true, default );
                await SendAsync( client, "no-message-property", "1" );
                await client.SendAsync( Encoding.UTF8.GetBytes( """{"message":1}""" ), WebSocketMessageType.Text, true, default );
                // A well formed one right after proves the connection went through all of it.
                await SendAsync( client, "OD", "1" );

                await good.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                host.Manager.TryGetConnection( connectionId, out _ ).ShouldBeTrue( "The connection survived the garbage." );
            }
        }
        finally
        {
            host.Manager.MessageReceived.Sync -= onMessage;
        }
    }

    [Test]
    public async Task with_no_subscriber_incoming_messages_are_not_even_read_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        await using var host = await WebSocketChannelHost.StartAsync( map );

        var (client, connectionId) = await host.ConnectAsync();
        using( client )
        {
            // Nothing subscribes: the manager must not so much as parse this. Sending garbage is how we
            // check it - a parse would log a warning, and any throw would end the connection.
            await client.SendAsync( Encoding.UTF8.GetBytes( "not json at all" ), WebSocketMessageType.Text, true, default );

            // The descending direction still works, which is the proof the connection is untouched.
            await host.Manager.SendAsync( connectionId, "OD", Encoding.UTF8.GetBytes( """{"ok":true}""" ) );
            using var frame = await WebSocketChannelHost.ReceiveJsonAsync( client );
            frame.RootElement.GetProperty( "topic" ).GetString().ShouldBe( "OD" );
        }
    }
}
