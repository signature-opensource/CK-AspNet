using System.Buffers;

namespace CK.WebSocket;

public interface IMessageWriter<in TMessage>
{
    void WriteMessage(TMessage message, IBufferWriter<byte> output);
}
