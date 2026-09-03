using CK.Core;
using CK.PerfectEvent;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Shouldly;

namespace CK.AspNet.WebSocketChannel.Tests;

/// <summary>
/// Behaviour of the channel itself: the envelope, the routing by topic, and the guarantees a feature
/// relies on when it shares the socket with others.
/// </summary>
[TestFixture]
public class WebSocketChannelTests
{
    // The very frame CK.Observable.WebSocketWatcher writes for a transaction event, and the one
    // CK.AspNet.SessionChannel writes for a banishment. They are here verbatim on purpose: the
    // envelope must carry them untouched.
    const string ObservableDomainFrame = """["D",{"N":12,"E":[["I",1,"P",0]],"L":11}]""";
    const string SessionChannelFrame = """{"type":"banned"}""";

    static ReadOnlyMemory<byte> Utf8( string s ) => Encoding.UTF8.GetBytes( s );

    [Test]
    public async Task two_topics_share_one_socket_and_payloads_are_embedded_verbatim_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        await using var host = await WebSocketChannelHost.StartAsync( map );

        var (client, connectionId) = await host.ConnectAsync();
        using( client )
        {
            // Two features, one connection object each of them holds: both push on it under their topic.
            var connection = host.GetConnection( connectionId );
            await connection.WriteAsync( "OD", Utf8( ObservableDomainFrame ) );
            await connection.WriteAsync( "SC", Utf8( SessionChannelFrame ) );

            using( var first = await WebSocketChannelHost.ReceiveJsonAsync( client ) )
            {
                first.RootElement.GetProperty( "topic" ).GetString().ShouldBe( "OD" );
                first.RootElement.GetProperty( "message" ).GetRawText().ShouldBe( ObservableDomainFrame,
                    "The envelope only wraps: what a feature writes is what its handler receives." );
            }
            using( var second = await WebSocketChannelHost.ReceiveJsonAsync( client ) )
            {
                second.RootElement.GetProperty( "topic" ).GetString().ShouldBe( "SC" );
                second.RootElement.GetProperty( "message" ).GetRawText().ShouldBe( SessionChannelFrame );
            }
        }
    }

    [Test]
    public async Task writing_on_a_closed_connection_is_a_silent_no_op_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        await using var host = await WebSocketChannelHost.StartAsync( map );

        // A feature that pushes to a user which is simply not connected finds no connection at all.
        host.Manager.TryGetConnection( "no-such-connection", out _ ).ShouldBeFalse();

        var (client, connectionId) = await host.ConnectAsync();
        // A feature holds the connection object: it must stay harmless once the socket is gone.
        var connection = host.GetConnection( connectionId );
        using( client )
        {
            var closed = host.WaitForCloseAsync( connectionId );
            // Abort rather than a close handshake: the server tears the socket down at once
            // (CloseTimeout is zero, so shutdown never waits on a client), and this is anyway the
            // realistic case - a closed tab, a lost network.
            client.Abort();
            await closed;
        }
        // Pushing onto the connection that just went away: normal race, not an error, whichever overload.
        connection.IsDisposed.ShouldBeTrue();
        await Should.NotThrowAsync( async () => await connection.WriteAsync( "OD", Utf8( SessionChannelFrame ) ) );
        await Should.NotThrowAsync( async () => await connection.WriteAsync( Utf8( SessionChannelFrame ) ) );
        host.Manager.TryGetConnection( connectionId, out _ ).ShouldBeFalse();
    }

    [Test]
    public async Task writes_racing_a_disconnection_never_throw_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        await using var host = await WebSocketChannelHost.StartAsync( map );

        var (client, connectionId) = await host.ConnectAsync();
        var connection = host.GetConnection( connectionId );
        using( client )
        {
            // A feature pushes continuously while the client goes away: whatever the interleaving with the
            // dispose, no write may surface an exception. This is the contract every push relies on.
            var closed = host.WaitForCloseAsync( connectionId );
            using var stop = new CancellationTokenSource();
            var pusher = Task.Run( async () =>
            {
                while( !stop.IsCancellationRequested )
                {
                    await connection.WriteAsync( "OD", Utf8( ObservableDomainFrame ) );
                    await connection.WriteAsync( Utf8( SessionChannelFrame ) );
                }
            } );
            client.Abort();
            await closed;
            // Keep pushing a little after the close so the post-dispose path is exercised too.
            await Task.Delay( 100 );
            stop.Cancel();
            await Should.NotThrowAsync( async () => await pusher );
        }
        connection.IsDisposed.ShouldBeTrue();
    }

    [Test]
    public async Task a_faulty_handler_does_not_tear_down_the_shared_socket_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        await using var host = await WebSocketChannelHost.StartAsync( map );

        // One feature is broken. Every other feature must keep its channel: that is what SafeRaiseAsync buys.
        SequentialEventHandler<WebSocketChannelConnection> faulty = ( monitor, c ) => throw new CKException( "Deliberate." );
        host.Manager.ConnectionOpened.Sync += faulty;
        try
        {
            var (client, connectionId) = await host.ConnectAsync();
            using( client )
            {
                await host.GetConnection( connectionId ).WriteAsync( "OD", Utf8( ObservableDomainFrame ) );
                using var received = await WebSocketChannelHost.ReceiveJsonAsync( client );
                received.RootElement.GetProperty( "topic" ).GetString().ShouldBe( "OD" );
            }
        }
        finally
        {
            host.Manager.ConnectionOpened.Sync -= faulty;
        }
    }

    [Test]
    public async Task the_connection_is_already_gone_when_ConnectionClosed_is_raised_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        await using var host = await WebSocketChannelHost.StartAsync( map );

        // The connection a feature holds. Assigned once connected, read by the closed handler below,
        // which can only run after the abort.
        WebSocketChannelConnection? connection = null;
        var seen = new TaskCompletionSource<(bool StillThere, bool SendThrew)>( TaskCreationOptions.RunContinuationsAsynchronously );
        AsyncSequentialEventHandler<ConnectionClosedEvent> onClosed = async ( monitor, e, cancel ) =>
        {
            // What a feature does here is release its own state. Talking to the socket must be a
            // harmless no-op, not an ObjectDisposedException.
            bool stillThere = host.Manager.TryGetConnection( e.ConnectionId, out _ );
            bool threw = false;
            try
            {
                await connection!.WriteAsync( "OD", Utf8( SessionChannelFrame ) );
            }
            catch
            {
                threw = true;
            }
            seen.TrySetResult( (stillThere, threw) );
        };
        host.Manager.ConnectionClosed.Async += onClosed;
        try
        {
            var (client, connectionId) = await host.ConnectAsync();
            connection = host.GetConnection( connectionId );
            using( client )
            {
                client.Abort();
            }
            var result = await seen.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            result.StillThere.ShouldBeFalse( "The connection is removed from the manager before the event is raised." );
            result.SendThrew.ShouldBeFalse( "Writing from a ConnectionClosed handler must be a silent no-op." );
        }
        finally
        {
            host.Manager.ConnectionClosed.Async -= onClosed;
        }
    }
}
