using CK.Core;
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
/// </summary>
public sealed class WebSocketChannelConnection : IAsyncDisposable
{
    readonly IWebsocketConnectionContext<ReadOnlyMemory<byte>> _connection;
    readonly SemaphoreSlim _writeLock;
    // Guards against in-flight pushes writing to a disposed connection, and prevents double-dispose
    // of the semaphore if disposal paths ever overlap.
    volatile bool _disposed;

    internal WebSocketChannelConnection( IWebsocketConnectionContext<ReadOnlyMemory<byte>> connection )
    {
        _connection = connection;
        _writeLock = new SemaphoreSlim( 1, 1 );
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

    internal IActivityMonitor Monitor { get; }

    /// <summary>
    /// Writes a message to the client. Silently does nothing once the connection has been disposed:
    /// a push racing with a disconnection is normal, not an error.
    /// <para>
    /// Prefer <see cref="WebSocketChannelManager.SendAsync(string, string, ReadOnlyMemory{byte})"/>:
    /// it is the only place that knows the envelope. This method writes the bytes as they are given.
    /// </para>
    /// </summary>
    /// <param name="message">The raw bytes to write.</param>
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
    /// Aborts the connection (idempotent): cancels the pending SimpleR read and drives the normal
    /// disconnect path, so on host shutdown Kestrel drains immediately instead of waiting out
    /// <c>HostOptions.ShutdownTimeout</c>.
    /// </summary>
    public void Abort() => _connection.Abort();

    /// <summary>
    /// Marks this connection as disposed and releases the write lock. Idempotent.
    /// <para>
    /// The manager disposes the connection <em>before</em> raising its closed event, so that any write
    /// attempted from a handler is a silent no-op rather than a write onto a socket that is already gone.
    /// </para>
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if( _disposed ) return ValueTask.CompletedTask; // Already disposed.
        _disposed = true;
        _writeLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
