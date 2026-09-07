namespace CK.WebSocket;

/// <summary>Protocol that reads and writes messages using distinct input and output types.</summary>
public interface IMessageProtocol<TMessageIn, in TMessageOut> : IMessageReader<TMessageIn>, IMessageWriter<TMessageOut>;

/// <summary>Protocol that reads and writes messages of a single type.</summary>
public interface IMessageProtocol<TMessage> : IMessageProtocol<TMessage, TMessage>;
