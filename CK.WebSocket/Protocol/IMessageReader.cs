using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace CK.WebSocket;

/// <summary>Reads a message out of a byte sequence.</summary>
public interface IMessageReader<TMessage>
{
    /// <summary>Attempts to parse a complete message out of the input sequence.</summary>
    bool TryParseMessage(ref ReadOnlySequence<byte> input, [NotNullWhen(true)]out TMessage message);
}
