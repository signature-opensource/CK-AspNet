using CK.AspNet.WebSocket;
using NUnit.Framework;
using Shouldly;
using System.Buffers;
using System.Text;

namespace CK.AspNet.WebSocket.Tests;

[TestFixture]
public class FrameProtocolTests
{
    [Test]
    public void single_frame_message_round_trips()
    {
        var protocol = new EndOfMessageDelimitedProtocol<string, string>( new TextProtocol() );
        var buffer = new ArrayBufferWriter<byte>();
        protocol.WriteMessage( "Hello", buffer );

        var input = new ReadOnlySequence<byte>( buffer.WrittenMemory );
        protocol.TryParseMessage( ref input, out var message ).ShouldBeTrue();
        message.ShouldBe( "Hello" );
        input.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void message_split_across_frames_is_reassembled()
    {
        // Two frames, only the second carries the end-of-message marker: this is what the transport
        // produces when a websocket message arrives split across several websocket frames.
        var buffer = new ArrayBufferWriter<byte>();
        var frameWriter = new FrameBufferWriter( buffer );
        WriteFrame( frameWriter, "Hel" );
        WriteFrame( frameWriter, "lo" );
        frameWriter.FinishLastFrame( isEndOfMessage: true );

        var protocol = new EndOfMessageDelimitedProtocol<string, string>( new TextProtocol() );
        var input = new ReadOnlySequence<byte>( buffer.WrittenMemory );
        protocol.TryParseMessage( ref input, out var message ).ShouldBeTrue();
        message.ShouldBe( "Hello" );
    }

    [Test]
    public void incomplete_input_is_not_parsed()
    {
        var protocol = new EndOfMessageDelimitedProtocol<string, string>( new TextProtocol() );
        var buffer = new ArrayBufferWriter<byte>();
        protocol.WriteMessage( "Hello", buffer );

        // Truncated payload: the parser must wait for more data, not throw.
        var input = new ReadOnlySequence<byte>( buffer.WrittenMemory[..^2] );
        protocol.TryParseMessage( ref input, out _ ).ShouldBeFalse();
    }

    [Test]
    public void frame_reader_reads_frames_and_their_end_of_message_marker()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var frameWriter = new FrameBufferWriter( buffer );
        WriteFrame( frameWriter, "one" );
        WriteFrame( frameWriter, "two" );
        frameWriter.FinishLastFrame( isEndOfMessage: true );

        var reader = new FrameReader();
        var input = new ReadOnlySequence<byte>( buffer.WrittenMemory );

        reader.ReadFrame( ref input, out var frame, out var isEndOfMessage ).ShouldBeTrue();
        Encoding.UTF8.GetString( frame ).ShouldBe( "one" );
        isEndOfMessage.ShouldBeFalse();

        reader.ReadFrame( ref input, out frame, out isEndOfMessage ).ShouldBeTrue();
        Encoding.UTF8.GetString( frame ).ShouldBe( "two" );
        isEndOfMessage.ShouldBeTrue();

        reader.ReadFrame( ref input, out _, out _ ).ShouldBeFalse();
    }

    static void WriteFrame( FrameBufferWriter writer, string content )
    {
        var bytes = Encoding.UTF8.GetBytes( content );
        var span = writer.GetSpan( bytes.Length );
        bytes.CopyTo( span );
        writer.Advance( bytes.Length );
    }
}
