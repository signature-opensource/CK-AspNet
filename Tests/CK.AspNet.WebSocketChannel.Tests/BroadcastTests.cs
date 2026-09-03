using System;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Shouldly;

namespace CK.AspNet.WebSocketChannel.Tests;

/// <summary>
/// One message for every client at once, built once.
/// </summary>
[TestFixture]
public class BroadcastTests
{
    // The frame CK.AspNet.SessionChannel would push to everyone.
    const string Frame = """{"type":"maintenance"}""";

    static ReadOnlyMemory<byte> Utf8( string s ) => Encoding.UTF8.GetBytes( s );

    [Test]
    public async Task a_topic_broadcast_reaches_every_connection_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        await using var host = await WebSocketChannelHost.StartAsync( map );

        var (clientA, _) = await host.ConnectAsync();
        var (clientB, _) = await host.ConnectAsync();
        using( clientA )
        using( clientB )
        {
            await host.Manager.SendBroadcastAsync( "SC", Utf8( Frame ) );

            foreach( var client in new[] { clientA, clientB } )
            {
                using var received = await WebSocketChannelHost.ReceiveJsonAsync( client );
                received.RootElement.GetProperty( "topic" ).GetString().ShouldBe( "SC" );
                received.RootElement.GetProperty( "message" ).GetRawText().ShouldBe( Frame );
            }
        }
    }

    [Test]
    public async Task a_raw_broadcast_writes_the_bytes_as_given_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        await using var host = await WebSocketChannelHost.StartAsync( map );

        var (clientA, _) = await host.ConnectAsync();
        var (clientB, _) = await host.ConnectAsync();
        using( clientA )
        using( clientB )
        {
            // Built once by the feature, written as-is to everyone: this is what the raw overload is for.
            var frame = WebSocketChannelEnvelope.Create( "SC", Utf8( Frame ) );
            await host.Manager.SendBroadcastAsync( frame );

            var expected = Encoding.UTF8.GetString( frame.Span );
            foreach( var client in new[] { clientA, clientB } )
            {
                using var received = await WebSocketChannelHost.ReceiveJsonAsync( client );
                received.RootElement.GetRawText().ShouldBe( expected );
            }
        }
    }

    [Test]
    public async Task broadcasting_with_no_connection_is_a_no_op_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        await using var host = await WebSocketChannelHost.StartAsync( map );

        host.Manager.Count.ShouldBe( 0 );
        await Should.NotThrowAsync( async () => await host.Manager.SendBroadcastAsync( "SC", Utf8( Frame ) ) );
    }

    [Test]
    public async Task a_broadcast_reaches_the_remaining_connections_after_a_close_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        await using var host = await WebSocketChannelHost.StartAsync( map );

        var (clientA, idA) = await host.ConnectAsync();
        var (clientB, _) = await host.ConnectAsync();
        using( clientB )
        {
            using( clientA )
            {
                var closed = host.WaitForCloseAsync( idA );
                clientA.Abort();
                await closed;
            }
            // A client left: the broadcast neither fails nor waits on it.
            await Should.NotThrowAsync( async () => await host.Manager.SendBroadcastAsync( "SC", Utf8( Frame ) ) );

            using var received = await WebSocketChannelHost.ReceiveJsonAsync( clientB );
            received.RootElement.GetProperty( "topic" ).GetString().ShouldBe( "SC" );
        }
    }
}
