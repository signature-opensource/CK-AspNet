using CK.Core;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace CK.WebSocket;

internal class WebSocketConnectionManager
{
    private readonly ConcurrentDictionary<string, WebSocketConnectionContext> _connections = new();

    internal WebSocketConnectionContext CreateConnection(IActivityMonitor monitor, HttpContext httpContext, WebSocketConnectionDispatcherOptions options)
    {
        var id = MakeNewConnectionId();
        // The connection monitor is the request scoped monitor (the one CKBuild registers): a WebSocket
        // connection is one long-lived request. It is required, as it is for CK.Cris.AspNet.

        monitor.Info($"New connection '{id}' created.");
        var pair = DuplexPipe.CreateConnectionPair(options.TransportPipeOptions, options.AppPipeOptions);
        var connection = new WebSocketConnectionContext(id, httpContext, pair.Application, pair.Transport, options);

        _connections.TryAdd(id, connection);

        return connection;

        static string MakeNewConnectionId()
        {
            // 128 bit buffer / 8 bits per byte = 16 bytes
            Span<byte> buffer = stackalloc byte[16];
            // Generate the id with RNGCrypto because we want a cryptographically random id, which GUID is not
            RandomNumberGenerator.Fill(buffer);
            return WebEncoders.Base64UrlEncode(buffer);
        }
    }

    internal async Task DisposeAndRemoveAsync(WebSocketConnectionContext connection, bool closeGracefully)
    {
        try
        {
            await connection.DisposeAsync(closeGracefully);
        }
        catch (IOException ex)
        {
            ActivityMonitor.StaticLogger.Debug($"Connection '{connection.ConnectionId}' was reset.", ex);
        }
        catch (WebSocketException ex) when (ex.InnerException is IOException)
        {
            ActivityMonitor.StaticLogger.Debug($"Connection '{connection.ConnectionId}' was reset.", ex);
        }
        catch (Exception ex)
        {
            ActivityMonitor.StaticLogger.Error($"Failed disposing connection '{connection.ConnectionId}'.", ex);
        }
        finally
        {
            // Remove it from the list after disposal so that's it's easy to see
            // connections that might be in a hung state via the connections list
            if (_connections.TryRemove(connection.ConnectionId, out var _))
            {
                ActivityMonitor.StaticLogger.Trace($"Removing connection '{connection.ConnectionId}' from the list of connections.");
            }
        }
    }
}
