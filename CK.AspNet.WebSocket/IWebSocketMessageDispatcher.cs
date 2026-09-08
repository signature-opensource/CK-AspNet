using System;
using System.Threading.Tasks;
using CK.Core;

namespace CK.AspNet.WebSocket;

/// <summary>
/// Message dispatcher for handling websocket connections.
/// </summary>
/// <typeparam name="TMessageIn">Input Message type</typeparam>
/// <typeparam name="TMessageOut">Output Message type</typeparam>
public interface IWebSocketMessageDispatcher<in TMessageIn, out TMessageOut>
{
    /// <summary>
    /// Called when a connection is established.
    /// </summary>
    /// <param name="connection">The connection.</param>
    Task OnConnectedAsync(IActivityMonitor monitor, IWebSocketConnectionContext<TMessageOut> connection);

    /// <summary>
    /// Called when a connection is disconnected.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="exception">The exception that occurred, if any.</param>
    Task OnDisconnectedAsync(IActivityMonitor monitor, IWebSocketConnectionContext<TMessageOut> connection, Exception? exception);

    /// <summary>
    /// Dispatches a message to the application.
    /// </summary>
    /// <param name="monitor"></param>
    /// <param name="connection">The connection.</param>
    /// <param name="message">The message to dispatch.</param>
    Task DispatchMessageAsync(IActivityMonitor monitor, IWebSocketConnectionContext<TMessageOut> connection, TMessageIn message);


    /// <summary>
    /// Handle errors that happened while parsing messages
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="exception">The exception that occurred.</param>
    Task OnParsingIssueAsync(IActivityMonitor monitor, IWebSocketConnectionContext<TMessageOut> connection, Exception exception );
}
