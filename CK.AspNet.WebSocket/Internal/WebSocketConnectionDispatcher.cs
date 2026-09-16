using System;
using System.Threading.Tasks;
using CK.Core;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;

namespace CK.AspNet.WebSocket;

internal class WebSocketConnectionDispatcher
{
    public WebSocketConnectionDispatcher(WebSocketConnectionManager connectionManager)
    {
        ConnectionManager = connectionManager;
    }

    private WebSocketConnectionManager ConnectionManager { get; }

    /// <summary>
    /// The initialization of the WebSocket connection.
    /// </summary>
    /// <param name="httpContext"></param>
    /// <param name="options"></param>
    /// <param name="connectionDelegate"></param>
    public async Task ExecuteAsync( IActivityMonitor monitor, HttpContext httpContext, WebSocketConnectionDispatcherOptions options, Func<IActivityMonitor, ConnectionContext, Task> connectionDelegate )
    {

        var connection = ConnectionManager.CreateConnection( monitor, httpContext, options );

        var transport = new WebSocketsServerTransport( options.WebSockets, connection.Application, connection );

        if( connection.TryActivateConnection( monitor, connectionDelegate, transport, httpContext ) )
        {
            // Wait for any of them to end
            await Task.WhenAny( connection.ApplicationTask!, connection.TransportTask! );

            await ConnectionManager.DisposeAndRemoveAsync( connection, closeGracefully: true );
        }
    }
}
