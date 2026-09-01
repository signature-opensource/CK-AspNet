using System.Buffers;

namespace CK.WebSocket;

public interface IDelimitedMessageReader<out TMessageIn>
{
    TMessageIn ParseMessage(ref ReadOnlySequence<byte> input);
}
