using System.Buffers;

namespace CK.WebSocket;

/// <summary>Reads a message that may span several delimited frames.</summary>
public interface IDelimitedMessageReader<out TMessageIn>
{
    /// <summary>Parses a message out of the accumulated frame input.</summary>
    TMessageIn ParseMessage(ref ReadOnlySequence<byte> input);
}
