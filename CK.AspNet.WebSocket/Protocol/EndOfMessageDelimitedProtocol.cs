using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace CK.AspNet.WebSocket;

/// <summary>Message protocol that delimits messages using an end-of-message marker on each frame.</summary>
public class EndOfMessageDelimitedProtocol<TMessageIn, TMessageOut> : IMessageProtocol<TMessageIn, TMessageOut>
{
    private readonly IDelimitedMessageProtocol<TMessageIn, TMessageOut> _innerProtocol;
    private readonly FrameReader _frameReader;

    /// <summary>Initializes a new instance wrapping the given delimited message protocol.</summary>
    public EndOfMessageDelimitedProtocol(IDelimitedMessageProtocol<TMessageIn, TMessageOut> innerProtocol)
    {
        _innerProtocol = innerProtocol;
        _frameReader = new FrameReader();
    }

    /// <summary>Reads frames from the input until a complete end-of-message-delimited message is available.</summary>
    public bool TryParseMessage(ref ReadOnlySequence<byte> input, [NotNullWhen(true)]out TMessageIn message)
    {
        var messageSequenceBuilder = new ReadOnlySequenceBuilder<byte>();
        var currentInput = input;
        while (_frameReader.ReadFrame(ref currentInput, out var packet, out var isEndOfMessage))
        {
            messageSequenceBuilder.Append(packet);
            if (isEndOfMessage)
            {
                input = currentInput;
                var messageSequence = messageSequenceBuilder.Build();
                message = _innerProtocol.ParseMessage(ref messageSequence);
#pragma warning disable CS8762 // Parameter must have a non-null value when exiting in some condition.
                return true;
#pragma warning restore CS8762 // Parameter must have a non-null value when exiting in some condition.
            }
        }

        message = default!;
        return false;
    }

    /// <summary>Writes the message as a sequence of frames, marking the last one as end-of-message.</summary>
    public void WriteMessage(TMessageOut message, IBufferWriter<byte> output)
    {
        var frameWriter = new FrameBufferWriter(output);
        _innerProtocol.WriteMessage(message, frameWriter);
        frameWriter.FinishLastFrame(true);
    }
}
