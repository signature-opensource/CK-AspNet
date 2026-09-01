using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace CK.WebSocket;

public interface IMessageReader<TMessage>
{
    bool TryParseMessage(ref ReadOnlySequence<byte> input, [NotNullWhen(true)]out TMessage message);
}
