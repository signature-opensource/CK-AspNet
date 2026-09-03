using CK.Core;
using CK.PerfectEvent;
using SimpleR;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AspNet.WebSocketChannel;

/// <summary>
/// One open WebSocket connection of the channel.
/// <para>
/// A connection carries no identity: it is anonymous by construction, and whatever a feature needs to
/// know about who is behind it is bound afterwards, on the authenticated Cris channel, by the feature
/// itself (see the topic that needs it). Nothing here knows about any of that.
/// </para>
/// <para>
/// Writes are serialized: this socket is shared by every topic, so concurrent pushes from unrelated
/// features are the normal case, not an edge case.
/// </para>
/// <para>
/// What the client says arrives on <see cref="MessageReceived"/>, for this connection only.
/// </para>
/// </summary>
public sealed class WebSocketChannelConnection : IAsyncDisposable
{
    readonly IWebsocketConnectionContext<ReadOnlyMemory<byte>> _connection;
    readonly SemaphoreSlim _writeLock;
    readonly PerfectEventSender<MessageReceivedEvent> _messageReceived;
    // Guards against in-flight pushes writing to a disposed connection, and makes DisposeAsync
    // idempotent if disposal paths ever overlap.
    volatile bool _disposed;

    internal WebSocketChannelConnection( IWebsocketConnectionContext<ReadOnlyMemory<byte>> connection )
    {
        _connection = connection;
        _writeLock = new SemaphoreSlim( 1, 1 );
        _messageReceived = new PerfectEventSender<MessageReceivedEvent>();
        // One monitor for the whole lifetime of the connection: it correlates the open and close logs
        // of a socket. It is the monitor the manager raises its perfect events with, so a feature
        // handling them logs in the context of the connection it is reacting to. Only that lifecycle
        // path uses it, and SimpleR never overlaps the connect and disconnect calls of one connection,
        // so this non thread-safe monitor is never used concurrently.
        Monitor = new ActivityMonitor( $"WebSocket connection '{connection.ConnectionId}'." );
    }

    /// <summary>
    /// Gets the connection identifier, sent to the client as the first message of the connection.
    /// </summary>
    public string ConnectionId => _connection.ConnectionId;

    /// <summary>
    /// Gets whether this connection has been disposed. A disposed connection silently swallows writes.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Raised for each message the client of this connection sends, once its envelope has been read.
    /// This is the event a feature that works per connection subscribes to: it sees this client's traffic
    /// only, and its handlers are removed when the connection is disposed, so there is nothing to
    /// unsubscribe on close.
    /// <para>
    /// Handlers still filter on <see cref="MessageReceivedEvent.Topic"/>: every feature shares this socket.
    /// See <see cref="WebSocketChannelManager.AllMessagesReceived"/> for the topic namespace rules, and
    /// for the manager-wide event that sees every connection. For one message, the handlers of this event
    /// run first, then the manager-wide ones.
    /// </para>
    /// <para>
    /// As long as nobody subscribes here nor on the manager, incoming messages are not even read.
    /// <see cref="MessageReceivedEvent"/> carries nothing authenticated.
    /// </para>
    /// </summary>
    public PerfectEvent<MessageReceivedEvent> MessageReceived => _messageReceived.PerfectEvent;

    internal IActivityMonitor Monitor { get; }

    // Lets the manager skip reading the bytes when nobody listens on this connection either.
    internal bool HasMessageHandlers => _messageReceived.HasHandlers;

    // Safe: one faulty feature must not tear down a socket that the other features share.
    internal Task RaiseMessageReceivedAsync( MessageReceivedEvent e ) => _messageReceived.SafeRaiseAsync( Monitor, e );

    /// <summary>
    /// Writes a frame to the client as it is given. Silently does nothing once the connection has been
    /// disposed: a push racing with a disconnection is normal, not an error.
    /// <para>
    /// Prefer <see cref="WriteAsync(string, ReadOnlyMemory{byte})"/>, which builds the envelope. This
    /// overload is for a frame already built by <see cref="WebSocketChannelEnvelope.Create"/>, typically
    /// once for several connections: anything else is not routed by the client.
    /// </para>
    /// </summary>
    /// <param name="message">The frame bytes to write.</param>
    public async ValueTask WriteAsync( ReadOnlyMemory<byte> message )
    {
        if( _disposed ) return; // In-flight push after dispose: silently bail out.
        await _writeLock.WaitAsync().ConfigureAwait( false );
        try
        {
            if( _disposed ) return; // Dispose happened while waiting for the lock.
            await _connection.WriteAsync( message ).ConfigureAwait( false );
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Wraps the payload in the <c>{topic,message}</c> envelope and writes it: this is how a feature pushes
    /// to this client. Silently does nothing once the connection has been disposed: a push racing with a
    /// disconnection is normal, not an error.
    /// </summary>
    /// <param name="topic">The topic that routes the message on the client. Must not be null or white space.</param>
    /// <param name="message">The payload, as a JSON value. It is embedded as-is, not escaped.</param>
    public ValueTask WriteAsync( string topic, ReadOnlyMemory<byte> message )
    {
        // Disposed: bail out before building an envelope for nothing.
        if( _disposed ) return ValueTask.CompletedTask;
        return WriteAsync( WebSocketChannelEnvelope.Create( topic, message ) );
    }

    // Sent once, first, unenveloped: the client needs its identifier before anything else.
    internal ValueTask WriteNegotiationAsync() => WriteAsync( WebSocketChannelEnvelope.CreateNegotiation( ConnectionId ) );

    /// <summary>
    /// Aborts the connection (idempotent): cancels the pending SimpleR read and drives the normal
    /// disconnect path, so on host shutdown Kestrel drains immediately instead of waiting out
    /// <c>HostOptions.ShutdownTimeout</c>.
    /// </summary>
    public void Abort() => _connection.Abort();

    /// <summary>
    /// Marks this connection as disposed and clears the <see cref="MessageReceived"/> handlers. Idempotent.
    /// The write lock itself is never disposed: a write racing this call must stay a silent no-op, and the
    /// semaphore owns nothing that needs releasing.
    /// <para>
    /// The manager disposes the connection <em>before</em> raising its closed event, so that any write
    /// attempted from a handler is a silent no-op rather than a write onto a socket that is already gone.
    /// </para>
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if( _disposed ) return ValueTask.CompletedTask; // Already disposed.
        _disposed = true;
        // Handlers die with the connection: a feature that subscribed here has nothing to unsubscribe,
        // and whatever its closures captured is released.
        _messageReceived.RemoveAll();
        return ValueTask.CompletedTask;
    }
}
