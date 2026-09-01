namespace CK.WebSocket;

public interface IDelimitedMessageProtocol<out TMessageIn, in TMessageOut> : IDelimitedMessageReader<TMessageIn>, IMessageWriter<TMessageOut>;
public interface IDelimitedMessageProtocol<TMessage> : IDelimitedMessageProtocol<TMessage, TMessage>;
