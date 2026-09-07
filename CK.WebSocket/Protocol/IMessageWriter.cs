using System.Buffers;

namespace CK.WebSocket;

/// <summary>Writes a message to a buffer.</summary>
public interface IMessageWriter<in TMessage>
{
    /// <summary>Writes the message to the given output buffer.</summary>
    void WriteMessage(TMessage message, IBufferWriter<byte> output);
}
