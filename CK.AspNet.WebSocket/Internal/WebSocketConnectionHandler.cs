using System;
using CK.Core;
using System.Buffers;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;

namespace CK.AspNet.WebSocket;

internal class WebSocketConnectionHandler<TMessageIn, TMessageOut>
{
    private readonly IMessageProtocol<TMessageIn, TMessageOut> _messageProtocol;
    private readonly IWebSocketMessageDispatcher<TMessageIn, TMessageOut> _dispatcher;

    public WebSocketConnectionHandler(IMessageProtocol<TMessageIn, TMessageOut> messageProtocol,
        IWebSocketMessageDispatcher<TMessageIn, TMessageOut> messageDispatcher)
    {
        _messageProtocol = messageProtocol;
        _dispatcher = messageDispatcher;
    }

    public async Task OnConnectedAsync(IActivityMonitor monitor, ConnectionContext connection)
    {
        var appConnectionContext = new ApplicationConnectionContext<TMessageOut>(connection, _messageProtocol);

        try
        {
            // TODO: add lifetime manager
            await RunApplicationAsync(monitor, appConnectionContext);
        }
        finally
        {
            appConnectionContext.Cleanup();
        }
    }
    private async Task RunApplicationAsync(IActivityMonitor monitor, ApplicationConnectionContext<TMessageOut> connection)
    {
        try
        {
            await _dispatcher.OnConnectedAsync(monitor, connection);
        }
        catch (Exception ex)
        {
            ActivityMonitor.StaticLogger.Error("Error when dispatching 'OnConnectedAsync' on the dispatcher.", ex);

            // return instead of throw to let close message send successfully
            return;
        }

        try
        {
            await DispatchMessagesAsync(connection);
        }
        catch (OperationCanceledException)
        {
            // Don't treat OperationCanceledException as an error, it's basically a "control flow"
            // exception to stop things from running
        }
        catch (Exception ex)
        {
            ActivityMonitor.StaticLogger.Error("Error when processing requests.", ex);

            await OnDisconnectedAsync(monitor, connection, ex);

            return;
        }

        await OnDisconnectedAsync(monitor, connection, connection.CloseException);
    }

    private async Task OnDisconnectedAsync(IActivityMonitor monitor, ApplicationConnectionContext<TMessageOut> connection, Exception? exception)
    {
        // We wait on abort to complete, this is so that we can guarantee that all callbacks have fired
        // before OnDisconnectedAsync

        try
        {
            // Ensure the connection is aborted before firing disconnect
            await connection.AbortAsync();
        }
        finally
        {
            await _dispatcher.OnDisconnectedAsync(monitor, connection, exception);
        }
    }

    private async Task DispatchMessagesAsync(ApplicationConnectionContext<TMessageOut> connection)
    {
        var input = connection.Input;
        var monitor = new ActivityMonitor( $"Dispatching messaging loop for '{connection.ConnectionId}'." );

        while (true)
        {
            var result = await input.ReadAsync();
            var buffer = result.Buffer;
            try
            {
                if (result.IsCanceled)
                {
                    break;
                }

                while (!buffer.IsEmpty && TryParseMessageImpl(ref buffer, out var message, out var exception))
                {
                    if (exception == null)
                    {
                        await _dispatcher.DispatchMessageAsync(monitor, connection, message);
                    }
                    else
                    {
                        await _dispatcher.OnParsingIssueAsync(monitor, connection, exception);
                    }
                }

                if (result.IsCompleted)
                {
                    if (!buffer.IsEmpty)
                    {
                        throw new InvalidDataException("Connection terminated while reading a message.");
                    }
                    break;
                }
            }
            finally
            {
                // The buffer was sliced up to where it was consumed, so we can just advance to the start.
                // We mark examined as buffer.End so that if we didn't receive a full frame, we'll wait for more data
                // before yielding the read again.
                input.AdvanceTo(buffer.Start, buffer.End);
            }
        }
    }

    private bool TryParseMessageImpl(ref ReadOnlySequence<byte> buffer, out TMessageIn message, out Exception? exception)
    {
        try
        {
            var result =  _messageProtocol.TryParseMessage(ref buffer, out message);
            exception = null;
            return result;
        }
        catch (Exception ex)
        {
            exception = ex;
            message = default!;
            return true;
        }
    }
}
