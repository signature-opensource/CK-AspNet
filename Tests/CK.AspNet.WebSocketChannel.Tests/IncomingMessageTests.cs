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
/// The ascending direction: it exists only for whoever subscribes to a connection's
/// <see cref="WebSocketChannelConnection.MessageReceived"/> or to the manager-wide
/// <see cref="WebSocketChannelManager.AllMessagesReceived"/>, and it must never let a client harm the
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
        host.Manager.AllMessagesReceived.Async += onMessage;
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
            host.Manager.AllMessagesReceived.Async -= onMessage;
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
        host.Manager.AllMessagesReceived.Sync += onMessage;
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
            host.Manager.AllMessagesReceived.Sync -= onMessage;
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
        host.Manager.AllMessagesReceived.Sync += onMessage;
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
            host.Manager.AllMessagesReceived.Sync -= onMessage;
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
            await host.GetConnection( connectionId ).WriteAsync( "OD", Encoding.UTF8.GetBytes( """{"ok":true}""" ) );
            using var frame = await WebSocketChannelHost.ReceiveJsonAsync( client );
            frame.RootElement.GetProperty( "topic" ).GetString().ShouldBe( "OD" );
        }
    }

    [Test]
    public async Task a_connection_only_sees_its_own_messages_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        await using var host = await WebSocketChannelHost.StartAsync( map );

        var (clientA, idA) = await host.ConnectAsync();
        var (clientB, idB) = await host.ConnectAsync();
        using( clientA )
        using( clientB )
        {
            // A feature working per connection subscribes on the connection: no filter on the sender,
            // and nothing to unsubscribe, the handler dies with the connection.
            var seenByA = new ConcurrentQueue<string>();
            host.GetConnection( idA ).MessageReceived.Sync += ( monitor, e ) => seenByA.Enqueue( e.Connection.ConnectionId );

            // The manager-wide event sees everything.
            var seenByAll = new ConcurrentQueue<string>();
            var twoSeen = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
            SequentialEventHandler<MessageReceivedEvent> onAll = ( monitor, e ) =>
            {
                seenByAll.Enqueue( e.Connection.ConnectionId );
                if( seenByAll.Count == 2 ) twoSeen.TrySetResult();
            };
            host.Manager.AllMessagesReceived.Sync += onAll;
            try
            {
                await SendAsync( clientA, "OD", "1" );
                await SendAsync( clientB, "OD", "2" );
                await twoSeen.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );

                // Two sockets, two read loops: no order across connections.
                seenByAll.ShouldBe( [idA, idB], ignoreOrder: true );
                // A's handler ran before the manager-wide one saw A's message (see the order test), and
                // B's message never reaches it.
                seenByA.ShouldBe( [idA], Case.Sensitive, "A handler subscribed on A must not see B's traffic." );
            }
            finally
            {
                host.Manager.AllMessagesReceived.Sync -= onAll;
            }
        }
    }

    [Test]
    public async Task connection_handlers_run_before_manager_wide_handlers_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        await using var host = await WebSocketChannelHost.StartAsync( map );

        var (client, connectionId) = await host.ConnectAsync();
        using( client )
        {
            var order = new ConcurrentQueue<string>();
            var done = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
            host.GetConnection( connectionId ).MessageReceived.Sync += ( monitor, e ) => order.Enqueue( "connection" );
            SequentialEventHandler<MessageReceivedEvent> onAll = ( monitor, e ) =>
            {
                order.Enqueue( "manager" );
                done.TrySetResult();
            };
            host.Manager.AllMessagesReceived.Sync += onAll;
            try
            {
                await SendAsync( client, "OD", "1" );
                await done.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                order.ShouldBe( ["connection", "manager"], Case.Sensitive, "The connection's handlers run first: this order is a contract." );
            }
            finally
            {
                host.Manager.AllMessagesReceived.Sync -= onAll;
            }
        }
    }

    [Test]
    public async Task a_connection_subscriber_alone_turns_reading_on_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        await using var host = await WebSocketChannelHost.StartAsync( map );

        var (client, connectionId) = await host.ConnectAsync();
        using( client )
        {
            // No manager-wide subscriber: a subscriber on the connection alone must be enough for the
            // bytes to be read. Otherwise a per-connection feature would silently never hear anything.
            var received = new TaskCompletionSource<string>( TaskCreationOptions.RunContinuationsAsynchronously );
            host.GetConnection( connectionId ).MessageReceived.Sync += ( monitor, e ) => received.TrySetResult( e.Topic );

            await SendAsync( client, "OD", "1" );
            var topic = await received.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            topic.ShouldBe( "OD" );
        }
    }
}
