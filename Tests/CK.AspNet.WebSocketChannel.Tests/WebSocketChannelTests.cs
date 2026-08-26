using CK.Core;
using CK.PerfectEvent;
using System;
using System.Text;
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
            await host.Manager.SendAsync( connectionId, "OD", Utf8( ObservableDomainFrame ) );
            await host.Manager.SendAsync( connectionId, "SC", Utf8( SessionChannelFrame ) );

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
    public async Task sending_to_an_unknown_or_closed_connection_is_a_silent_no_op_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        await using var host = await WebSocketChannelHost.StartAsync( map );

        // A feature that pushes to a user which is simply not connected must not have to check first.
        await Should.NotThrowAsync( async () => await host.Manager.SendAsync( "no-such-connection", "OD", Utf8( SessionChannelFrame ) ) );

        var (client, connectionId) = await host.ConnectAsync();
        using( client )
        {
            var closed = WaitForCloseAsync( host.Manager, connectionId );
            // Abort rather than a close handshake: the server tears the socket down at once
            // (CloseTimeout is zero, so shutdown never waits on a client), and this is anyway the
            // realistic case - a closed tab, a lost network.
            client.Abort();
            await closed;
        }
        // Pushing onto the connection that just went away: normal race, not an error.
        await Should.NotThrowAsync( async () => await host.Manager.SendAsync( connectionId, "OD", Utf8( SessionChannelFrame ) ) );
        host.Manager.TryGetConnection( connectionId, out _ ).ShouldBeFalse();
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
                await host.Manager.SendAsync( connectionId, "OD", Utf8( ObservableDomainFrame ) );
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

        var seen = new TaskCompletionSource<(bool StillThere, bool SendThrew)>( TaskCreationOptions.RunContinuationsAsynchronously );
        AsyncSequentialEventHandler<ConnectionClosedEvent> onClosed = async ( monitor, e, cancel ) =>
        {
            // What a feature does here is release its own state. Talking to the socket must be a
            // harmless no-op, not an ObjectDisposedException.
            bool stillThere = host.Manager.TryGetConnection( e.ConnectionId, out _ );
            bool threw = false;
            try
            {
                await host.Manager.SendAsync( e.ConnectionId, "OD", Utf8( SessionChannelFrame ) );
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
            var (client, _) = await host.ConnectAsync();
            using( client )
            {
                client.Abort();
            }
            var result = await seen.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            result.StillThere.ShouldBeFalse( "The connection is removed from the manager before the event is raised." );
            result.SendThrew.ShouldBeFalse( "Sending from a ConnectionClosed handler must be a silent no-op." );
        }
        finally
        {
            host.Manager.ConnectionClosed.Async -= onClosed;
        }
    }

    static Task WaitForCloseAsync( WebSocketChannelManager manager, string connectionId )
    {
        var tcs = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        SequentialEventHandler<ConnectionClosedEvent> handler = null!;
        handler = ( monitor, e ) =>
        {
            if( e.ConnectionId == connectionId )
            {
                manager.ConnectionClosed.Sync -= handler;
                tcs.TrySetResult();
            }
        };
        manager.ConnectionClosed.Sync += handler;
        return tcs.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
    }
}
