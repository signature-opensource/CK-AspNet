using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;

namespace CK.WebSocket;

internal class WebSocketConnectionDispatcher
{
    public WebSocketConnectionDispatcher(WebSocketConnectionManager connectionManager)
    {
        ConnectionManager = connectionManager;
    }

    private WebSocketConnectionManager ConnectionManager { get; }

    public async Task ExecuteAsync(HttpContext httpContext, WebSocketConnectionDispatcherOptions options, ConnectionDelegate connectionDelegate)
    {
        var connection = ConnectionManager.CreateConnection(httpContext, options);

        var transport = new WebSocketsServerTransport(options.WebSockets, connection.Application, connection);

        if (connection.TryActivateConnection(connectionDelegate, transport, httpContext))
        {
            // Wait for any of them to end
            await Task.WhenAny(connection.ApplicationTask!, connection.TransportTask!);

            await ConnectionManager.DisposeAndRemoveAsync(connection, closeGracefully: true);
        }
    }
}
