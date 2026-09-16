using NUnit.Framework;
using Shouldly;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AspNet.WebSocket.Tests;

/// <summary>
/// The custom (non-delimited) protocol path: UseCustomProtocol turns FramePackets off, so the transport
/// hands the raw byte stream to the parser and the protocol alone finds the message boundaries. The other
/// tests all use the delimited path, so this is the only cover of UseCustomProtocol.
/// </summary>
[TestFixture]
public class CustomProtocolTests
{
    [Test]
    public async Task a_custom_non_delimited_protocol_round_trips_Async()
    {
        var (app, baseUri) = await ConfigurableHost.StartAsync( b =>
        {
            b.UseCustomProtocol( new LengthPrefixedProtocol() );
            b.UseDispatcher( new EchoServer.EchoDispatcher() );
        } );
        await using var _ = app;

        using var client = new ClientWebSocket();
        await client.ConnectAsync( ConfigurableHost.WsUri( baseUri ), CancellationToken.None );

        (await RoundTripAsync( client, "Hello custom" )).ShouldBe( "Hello custom" );
        // A second message proves the length prefix delimits: the parser found the first boundary, stopped
        // there, and left the rest for the next read.
        (await RoundTripAsync( client, "second" )).ShouldBe( "second" );
    }

    // Sends one length-prefixed frame and returns the payload the server echoed, prefix stripped.
    static async Task<string> RoundTripAsync( ClientWebSocket client, string message )
    {
        var payload = Encoding.UTF8.GetBytes( message );
        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian( frame, payload.Length );
        payload.CopyTo( frame.AsSpan( 4 ) );
        await client.SendAsync( frame, WebSocketMessageType.Binary, true, CancellationToken.None );

        using var cts = new CancellationTokenSource( TimeSpan.FromSeconds( 5 ) );
        var buffer = new byte[1024];
        var received = await client.ReceiveAsync( buffer, cts.Token );
        received.EndOfMessage.ShouldBeTrue();
        var length = BinaryPrimitives.ReadInt32BigEndian( buffer.AsSpan( 0, 4 ) );
        return Encoding.UTF8.GetString( buffer, 4, length );
    }
}

/// <summary>
/// A self-delimiting protocol: a 4-byte big-endian length, then the UTF-8 payload. It is the kind of
/// protocol UseCustomProtocol exists for, where nothing frames the bytes for you.
/// </summary>
sealed class LengthPrefixedProtocol : IMessageProtocol<string>
{
    public bool TryParseMessage( ref ReadOnlySequence<byte> input, out string message )
    {
        message = null!;
        if( input.Length < 4 ) return false;
        Span<byte> header = stackalloc byte[4];
        input.Slice( 0, 4 ).CopyTo( header );
        var length = BinaryPrimitives.ReadInt32BigEndian( header );
        if( input.Length < 4 + length ) return false;
        message = Encoding.UTF8.GetString( input.Slice( 4, length ) );
        input = input.Slice( 4 + length );
        return true;
    }

    public void WriteMessage( string message, IBufferWriter<byte> output )
    {
        var payload = Encoding.UTF8.GetBytes( message );
        var span = output.GetSpan( 4 + payload.Length );
        BinaryPrimitives.WriteInt32BigEndian( span, payload.Length );
        payload.CopyTo( span.Slice( 4 ) );
        output.Advance( 4 + payload.Length );
    }
}
