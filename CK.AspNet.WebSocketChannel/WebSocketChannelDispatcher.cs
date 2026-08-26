using SimpleR;
using System;
using System.Buffers;
using System.Threading.Tasks;

namespace CK.AspNet.WebSocketChannel;

/// <summary>
/// The one SimpleR dispatcher of the application: it hands each connection over to the
/// <see cref="WebSocketChannelManager"/> and does nothing else. Features never see a dispatcher.
/// </summary>
public sealed class WebSocketChannelDispatcher : IWebSocketMessageDispatcher<ReadOnlySequence<byte>, ReadOnlyMemory<byte>>
{
    readonly WebSocketChannelManager _manager;

    public WebSocketChannelDispatcher( WebSocketChannelManager manager )
    {
        _manager = manager;
    }

    /// <summary>
    /// Registers the connection with the manager, which sends the identifier back and raises
    /// <see cref="WebSocketChannelManager.ConnectionOpened"/>.
    /// </summary>
    /// <param name="connection">The newly established connection.</param>
    public Task OnConnectedAsync( IWebsocketConnectionContext<ReadOnlyMemory<byte>> connection )
    {
        // The manager returns false (and has already aborted the connection) when the host is stopping
        // or the connection id collides. Nothing to do here in that case: the cancelled read loop ends
        // the connection.
        return _manager.OnConnectedAsync( connection );
    }

    /// <summary>
    /// Unregisters the connection from the manager, which disposes it and raises
    /// <see cref="WebSocketChannelManager.ConnectionClosed"/>.
    /// </summary>
    /// <param name="connection">The connection that is being disconnected.</param>
    /// <param name="exception">The exception that caused the disconnection, if any.</param>
    public Task OnDisconnectedAsync( IWebsocketConnectionContext<ReadOnlyMemory<byte>> connection, Exception? exception )
    {
        return _manager.OnDisconnectedAsync( connection.ConnectionId, exception );
    }

    /// <summary>
    /// Hands the message to the manager, which reads its envelope and raises
    /// <see cref="WebSocketChannelManager.MessageReceived"/> - and does neither as long as no feature
    /// subscribes, which is the usual case: what a client has to say normally travels on the
    /// authenticated Cris endpoint, where it is validated.
    /// <para>
    /// <paramref name="message"/> borrows the pipe's buffers (see
    /// <see cref="RawMessageProtocol.ParseMessage"/>), which is why it is passed on synchronously and
    /// copied there before any handler runs.
    /// </para>
    /// </summary>
    /// <param name="connection">The connection the message came from.</param>
    /// <param name="message">The received message.</param>
    public Task DispatchMessageAsync( IWebsocketConnectionContext<ReadOnlyMemory<byte>> connection, ReadOnlySequence<byte> message )
    {
        return _manager.OnMessageAsync( connection.ConnectionId, message );
    }
}
