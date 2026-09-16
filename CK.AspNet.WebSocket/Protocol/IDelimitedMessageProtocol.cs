namespace CK.AspNet.WebSocket;

/// <summary>Protocol that reads and writes messages using distinct input and output types, delimited across frames.</summary>
public interface IDelimitedMessageProtocol<out TMessageIn, in TMessageOut> : IDelimitedMessageReader<TMessageIn>, IMessageWriter<TMessageOut>;
/// <summary>Protocol that reads and writes messages of a single type, delimited across frames.</summary>
public interface IDelimitedMessageProtocol<TMessage> : IDelimitedMessageProtocol<TMessage, TMessage>;
