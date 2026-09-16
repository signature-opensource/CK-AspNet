using CK.Core;
using CK.PerfectEvent;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Shouldly;

namespace CK.AspNet.WebSocketChannel.Tests;

/// <summary>
/// The ascending twin of <c>WebSocketChannelTests.a_faulty_handler_does_not_tear_down_the_shared_socket</c>,
/// which covers a throwing descending handler (ConnectionOpened). A throwing incoming-message handler must
/// be just as harmless: the raise is safe (<see cref="WebSocketChannelConnection"/>.RaiseMessageReceivedAsync
/// uses SafeRaiseAsync), so one broken feature must not close the socket that every other feature shares.
/// <para>
/// The manager-wide <see cref="WebSocketChannelManager.AllMessagesReceived"/> and the per-connection
/// <see cref="WebSocketChannelConnection.MessageReceived"/> go through the same safe boundary, so this
/// covers both. The handler here is the manager-wide one: it sees every client, so it is the worse case.
/// </para>
/// </summary>
[TestFixture]
public class FaultyIncomingHandlerTests
{
    static ReadOnlyMemory<byte> Utf8( string s ) => Encoding.UTF8.GetBytes( s );

    static Task SendAsync( ClientWebSocket client, string topic, string message )
    {
        var frame = Encoding.UTF8.GetBytes( $"{{\"topic\":\"{topic}\",\"message\":{message}}}" );
        return client.SendAsync( frame, WebSocketMessageType.Text, true, CancellationToken.None );
    }

    [Test]
    public async Task a_throwing_incoming_message_handler_does_not_tear_down_the_shared_socket_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        await using var host = await WebSocketChannelHost.StartAsync( map );

        var (client, connectionId) = await host.ConnectAsync();
        using( client )
        {
            var connection = host.GetConnection( connectionId );

            // One feature's handler is broken: every incoming message hits it and it throws. It is the only
            // subscriber, so no handler order is in play - the read loop must swallow each throw on its own.
            // The count reaching two proves a second message is still read after the first throw.
            var throwCount = 0;
            var twoThrows = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
            SequentialEventHandler<MessageReceivedEvent> faulty = ( monitor, e ) =>
            {
                if( Interlocked.Increment( ref throwCount ) == 2 ) twoThrows.TrySetResult();
                throw new CKException( "Deliberate." );
            };
            host.Manager.AllMessagesReceived.Sync += faulty;
            try
            {
                await SendAsync( client, "OD", "1" );
                await SendAsync( client, "SC", "2" );

                // Both messages went through the throwing handler: the read loop survived each throw.
                await twoThrows.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );

                // The connection is still open, and the descending direction still works: the shared socket
                // is intact for every other feature.
                host.Manager.TryGetConnection( connectionId, out _ ).ShouldBeTrue( "A throwing handler must not close the connection." );
                await connection.WriteAsync( "OD", Utf8( """{"ok":true}""" ) );
                using var frame = await WebSocketChannelHost.ReceiveJsonAsync( client );
                frame.RootElement.GetProperty( "topic" ).GetString().ShouldBe( "OD" );
            }
            finally
            {
                host.Manager.AllMessagesReceived.Sync -= faulty;
            }
        }
    }
}
