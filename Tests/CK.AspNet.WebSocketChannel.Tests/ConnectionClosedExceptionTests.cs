using CK.PerfectEvent;
using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Shouldly;

namespace CK.AspNet.WebSocketChannel.Tests;

/// <summary>
/// The exception carried by <see cref="WebSocketChannelManager.ConnectionClosed"/>: null on a graceful
/// close, set when the connection ends abnormally. A feature reads it to tell a clean goodbye from a drop.
/// <para>
/// No subscriber is needed: the terminated-while-reading error rises from the core message loop, which
/// runs whether or not a feature listens, so these cover the channel with the socket purely descending.
/// </para>
/// </summary>
[TestFixture]
public class ConnectionClosedExceptionTests
{
    [Test]
    public async Task connection_closed_carries_no_exception_on_a_graceful_close_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        await using var host = await WebSocketChannelHost.StartAsync( map );

        var (client, connectionId) = await host.ConnectAsync();
        var closed = WaitForClosedEventAsync( host, connectionId );
        using( client )
        {
            await client.CloseOutputAsync( WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None );
            var e = await closed;
            e.Exception.ShouldBeNull( "A graceful close carries no exception." );
        }
    }

    [Test]
    public async Task connection_closed_carries_the_exception_on_an_abnormal_close_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        await using var host = await WebSocketChannelHost.StartAsync( map );

        var (client, connectionId) = await host.ConnectAsync();
        var closed = WaitForClosedEventAsync( host, connectionId );
        using( client )
        {
            // A frame that is never ended, then a close: the server finds a message left mid-read.
            await client.SendAsync( Encoding.UTF8.GetBytes( "partial" ), WebSocketMessageType.Text, endOfMessage: false, CancellationToken.None );
            await client.CloseOutputAsync( WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None );
            var e = await closed;
            e.Exception.ShouldBeOfType<InvalidDataException>(
                "A connection that ends mid-message surfaces the terminated-while-reading error." );
        }
    }

    // Captures the ConnectionClosed event for one connection, unlike the host's WaitForCloseAsync which only
    // signals: these tests assert on the exception the event carries.
    static Task<ConnectionClosedEvent> WaitForClosedEventAsync( WebSocketChannelHost host, string connectionId )
    {
        var tcs = new TaskCompletionSource<ConnectionClosedEvent>( TaskCreationOptions.RunContinuationsAsynchronously );
        SequentialEventHandler<ConnectionClosedEvent> handler = null!;
        handler = ( monitor, e ) =>
        {
            if( e.ConnectionId == connectionId )
            {
                host.Manager.ConnectionClosed.Sync -= handler;
                tcs.TrySetResult( e );
            }
        };
        host.Manager.ConnectionClosed.Sync += handler;
        return tcs.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
    }
}
