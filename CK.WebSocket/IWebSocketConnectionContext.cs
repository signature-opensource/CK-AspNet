using CK.Core;
using System.Security.Claims;

namespace CK.WebSocket;
/// <summary>
/// Encapsulates a websocket connection to a client.
/// </summary>
public interface IWebSocketConnectionContext
{
    /// <summary>
    /// Gets the unique identifier for the connection.
    /// </summary>
    string ConnectionId { get; }

    /// <summary>
    /// Gets the user associated with the connection.
    /// </summary>
    ClaimsPrincipal User { get; }

    /// <summary>
    /// Gets the connection monitor: the request scoped <see cref="IActivityMonitor"/> of the WebSocket
    /// upgrade request (the one <c>CKBuild</c> registers), that lives as long as the connection.
    /// <para>
    /// A monitor is not thread safe. The dispatcher callbacks of a connection never overlap, so they
    /// can use it directly. Anything else (typically a push to this connection from another thread)
    /// must log through <see cref="IActivityMonitor.ParallelLogger"/>. This is what the transport
    /// itself does, since it runs concurrently with the dispatcher.
    /// </para>
    /// </summary>
    IActivityMonitor Monitor { get; }

    /// <summary>
    /// Aborts the connection.
    /// </summary>
    void Abort();
}

/// <summary>
/// Encapsulates a websocket connection to a client.
/// </summary>
public interface IWebSocketConnectionContext<in TMessageOut> : IWebSocketConnectionContext
{
    /// <summary>
    /// Writes a message to the connection.
    /// </summary>
    /// <param name="message">The message to write.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    ValueTask WriteAsync(TMessageOut message, CancellationToken cancellationToken = default);
}
