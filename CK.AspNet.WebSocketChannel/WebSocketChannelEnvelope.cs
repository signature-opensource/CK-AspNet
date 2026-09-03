using CK.Core;
using System;
using System.Buffers;
using System.Text.Json;

namespace CK.AspNet.WebSocketChannel;

/// <summary>
/// The wire format of the channel: the <c>{topic,message}</c> envelope that routes every frame, and the
/// negotiation message that precedes them all. This is the only place that knows it, on both sides.
/// </summary>
public static class WebSocketChannelEnvelope
{
    /// <summary>
    /// Builds the <c>{topic,message}</c> frame. The payload is embedded as a JSON value, as-is and
    /// unvalidated: a feature hands over the very same bytes it would have written on its own socket.
    /// <para>
    /// Prefer <see cref="WebSocketChannelConnection.WriteAsync(string, ReadOnlyMemory{byte})"/> for one
    /// connection. Build the frame once with this method when the same message goes to several
    /// connections, and write it with <see cref="WebSocketChannelConnection.WriteAsync(ReadOnlyMemory{byte})"/>.
    /// </para>
    /// </summary>
    /// <param name="topic">The topic that routes the message on the client. Must not be null or white space.</param>
    /// <param name="message">The payload, as a JSON value.</param>
    /// <returns>The frame bytes.</returns>
    public static ReadOnlyMemory<byte> Create( string topic, ReadOnlyMemory<byte> message )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( topic );
        var buffer = new ArrayBufferWriter<byte>( message.Length + 32 );
        using( var writer = new Utf8JsonWriter( buffer ) )
        {
            writer.WriteStartObject();
            writer.WriteString( "topic", topic );
            writer.WritePropertyName( "message" );
            // Validation is skipped: the payload comes from our own writers.
            writer.WriteRawValue( message.Span, skipInputValidation: true );
            writer.WriteEndObject();
            writer.Flush();
        }
        return buffer.WrittenMemory;
    }

    // The first and only unenveloped message of a connection: the client needs its identifier to send
    // it back on the authenticated Cris channel, and it belongs to no topic.
    internal static ReadOnlyMemory<byte> CreateNegotiation( string connectionId )
    {
        var buffer = new ArrayBufferWriter<byte>( 64 );
        using( var writer = new Utf8JsonWriter( buffer ) )
        {
            writer.WriteStartObject();
            writer.WriteString( "connectionId", connectionId );
            writer.WriteEndObject();
            writer.Flush();
        }
        return buffer.WrittenMemory;
    }

    // Reads an incoming frame. Throws on anything that is not a {topic,message} envelope with a string
    // topic: a client can send anything, and the caller decides what to do with garbage.
    // The message is copied out of the document, and therefore out of the pipe's buffers, before
    // anything can await: this is what lets a handler keep the payload.
    internal static (string Topic, ReadOnlyMemory<byte> Message) Read( ReadOnlySequence<byte> input )
    {
        using var doc = JsonDocument.Parse( input );
        string topic = doc.RootElement.GetProperty( "topic" ).GetString()
                       ?? throw new JsonException( "Null topic." );
        var buffer = new ArrayBufferWriter<byte>( 256 );
        using( var writer = new Utf8JsonWriter( buffer ) )
        {
            doc.RootElement.GetProperty( "message" ).WriteTo( writer );
            writer.Flush();
        }
        return (topic, buffer.WrittenMemory);
    }
}
