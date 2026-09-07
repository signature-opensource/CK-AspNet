namespace CK.WebSocket;

/// <summary>Factory methods that compose a reader and a writer into a message protocol.</summary>
public static class MessageProtocol
{
    /// <summary>Creates a message protocol from a reader and a writer.</summary>
    public static IMessageProtocol<TMessageIn, TMessageOut> From<TMessageIn, TMessageOut>(
        IMessageReader<TMessageIn> reader, IMessageWriter<TMessageOut> writer)
        => new CompositeMessageProtocol<TMessageIn, TMessageOut>(reader, writer);

    /// <summary>Creates a delimited message protocol from a delimited reader and a writer.</summary>
    public static IDelimitedMessageProtocol<TMessageIn, TMessageOut> From<TMessageIn, TMessageOut>(
        IDelimitedMessageReader<TMessageIn> reader, IMessageWriter<TMessageOut> writer)
        => new CompositeDelimitedMessageProtocol<TMessageIn, TMessageOut>(reader, writer);
}
