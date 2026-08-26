using SimpleR.Protocol;
using System;
using System.Buffers;

namespace CK.AspNet.WebSocketChannel;

/// <summary>
/// SimpleR protocol of the channel: outgoing messages are written as raw bytes and incoming ones are
/// handed over as-is, without any decoding.
/// </summary>
public sealed class RawMessageProtocol : IDelimitedMessageProtocol<ReadOnlySequence<byte>, ReadOnlyMemory<byte>>
{
    /// <summary>
    /// Returns <paramref name="input"/> unchanged. Decoding here would allocate for every incoming
    /// message, including the usual case where nothing listens to them: the manager decides, and reads
    /// the bytes only once a feature has subscribed to
    /// <see cref="WebSocketChannelManager.MessageReceived"/>.
    /// <para>
    /// The returned sequence borrows the pipe's buffers: it is only valid until the read loop advances.
    /// That is why the manager copies the payload out of it synchronously, before any handler can await
    /// (see <see cref="MessageReceivedEvent.Message"/>).
    /// </para>
    /// </summary>
    /// <param name="input">The input sequence to parse the message from.</param>
    /// <returns>The <paramref name="input"/> itself.</returns>
    public ReadOnlySequence<byte> ParseMessage( ref ReadOnlySequence<byte> input ) => input;

    /// <summary>
    /// Writes a message to the output.
    /// </summary>
    /// <param name="message">The message to write.</param>
    /// <param name="output">The output buffer writer.</param>
    public void WriteMessage( ReadOnlyMemory<byte> message, IBufferWriter<byte> output )
    {
        output.Write( message.Span );
    }
}
