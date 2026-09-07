using AspNetTransferFormat = Microsoft.AspNetCore.Connections.TransferFormat;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using CK.Core;

namespace CK.WebSocket;

// Inside the CK.WebSocket namespace the simple name "WebSocket" resolves to the CK.WebSocket
// namespace itself: this alias (scoped to this namespace declaration) restores the type.
using WebSocket = System.Net.WebSockets.WebSocket;

internal sealed class WebSocketsServerTransport : IHttpTransport
{
    private readonly WebSocketTransportOptions _options;
    private readonly IActivityLineEmitter _logger;
    private readonly IDuplexPipe _application;
    private readonly WebSocketConnectionContext _connection;
    private volatile bool _aborted;
    private readonly FrameReader _frameReader;

    // Used to determine if the close was graceful or a network issue
    private bool _gracefulClose;

    public WebSocketsServerTransport(WebSocketTransportOptions options, IDuplexPipe application, WebSocketConnectionContext connection)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(application);

        _options = options;
        _application = application;
        _connection = connection;
        _frameReader = new FrameReader();
        
        // The receive and send loops run concurrently with each other and with the application:
        // only the thread safe parallel logger of the connection monitor can be used here.
        _logger = connection.Monitor.ParallelLogger;
    }

    public async Task<bool> ProcessRequestAsync(HttpContext context, CancellationToken token)
    {
        Debug.Assert(context.WebSockets.IsWebSocketRequest, "Not a websocket request");

        var subProtocol = _options.SubProtocolSelector?.Invoke(context.WebSockets.WebSocketRequestedProtocols);
        
        using (var ws = await context.WebSockets.AcceptWebSocketAsync(subProtocol))
        {
            _logger.Trace($"Socket opened using Sub-Protocol: '{subProtocol}'.");

            try
            {
                await ProcessSocketAsync(ws);
            }
            finally
            {
                _logger.Trace("Socket closed.");
            }
        }
        
        return _gracefulClose;
    }

    public async Task ProcessSocketAsync(WebSocket socket)
    {
        // Begin sending and receiving. Receiving must be started first because ExecuteAsync enables SendAsync.
        var receiving = StartReceiving(socket);
        var sending = StartSending(socket);

        // Wait for send or receive to complete
        var trigger = await Task.WhenAny(receiving, sending);

        if (trigger == receiving)
        {
            _logger.Trace("Waiting for the application to finish sending data.");

            // We're waiting for the application to finish and there are 2 things it could be doing
            // 1. Waiting for application data
            // 2. Waiting for a websocket send to complete

            // Cancel the application so that ReadAsync yields
            _application.Input.CancelPendingRead();

            using (var delayCts = new CancellationTokenSource())
            {
                var resultTask = await Task.WhenAny(sending, Task.Delay(_options.CloseTimeout, delayCts.Token));

                if (resultTask != sending)
                {
                    // We timed out so now we're in ungraceful shutdown mode
                    _logger.Trace("Timed out waiting for client to send the close frame, aborting the connection.");

                    // Abort the websocket if we're stuck in a pending send to the client
                    _aborted = true;

                    socket.Abort();
                }
                else
                {
                    delayCts.Cancel();
                }
            }
        }
        else
        {
            _logger.Trace("Waiting for the client to close the socket.");

            // We're waiting on the websocket to close and there are 2 things it could be doing
            // 1. Waiting for websocket data
            // 2. Waiting on a flush to complete (backpressure being applied)

            using (var delayCts = new CancellationTokenSource())
            {
                var resultTask = await Task.WhenAny(receiving, Task.Delay(_options.CloseTimeout, delayCts.Token));

                if (resultTask != receiving)
                {
                    // Abort the websocket if we're stuck in a pending receive from the client
                    _aborted = true;

                    socket.Abort();

                    // Cancel any pending flush so that we can quit
                    _application.Output.CancelPendingFlush();
                }
                else
                {
                    delayCts.Cancel();
                }
            }
        }
    }

    private async Task StartReceiving(WebSocket socket)
    {
        var token = _connection.Cancellation?.Token ?? default;
        try
        {
            while (!token.IsCancellationRequested)
            {
                // Do a 0 byte read so that idle connections don't allocate a buffer when waiting for a read
                var receiveResult = await socket.ReceiveAsync(Memory<byte>.Empty, token);

                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    _gracefulClose = true;
                    return;
                }
                
                var writer = _options.FramePackets ? (IBufferWriter<byte>)new FrameBufferWriter(_application.Output) : _application.Output;

                // if the empty read is a full message, proceed to frame+flush
                if (!receiveResult.EndOfMessage)
                {
                    var memory = writer.GetMemory();
                    
                    receiveResult = await socket.ReceiveAsync(memory, token);
                    
                    // Need to check again for netcoreapp3.0 and later because a close can happen between a 0-byte read and the actual read
                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        _gracefulClose = true;
                        return;
                    }
                }
                
                _logger.Debug($"Message received. Type: {receiveResult.MessageType}, size: {receiveResult.Count}, EndOfMessage: {receiveResult.EndOfMessage}.");

                writer.Advance(receiveResult.Count);
                if (writer is FrameBufferWriter frameWriter)
                {
                    frameWriter.FinishLastFrame(receiveResult.EndOfMessage);
                }
                
                var flushResult = await _application.Output.FlushAsync();

                // We canceled in the middle of applying back pressure
                // or if the consumer is done
                if (flushResult.IsCanceled || flushResult.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            // Client has closed the WebSocket connection without completing the close handshake
            _logger.Trace("Socket connection closed prematurely.", ex);
        }
        catch (OperationCanceledException)
        {
            // Ignore aborts, don't treat them like transport errors
        }
        catch (Exception ex)
        {
            if (!_aborted && !token.IsCancellationRequested)
            {
                _gracefulClose = true;
                _application.Output.Complete(ex);
            }
        }
        finally
        {
            if (_gracefulClose)
            {
                // We're done writing
                _application.Output.Complete();
            }
        }
    }

    private async Task StartSending(WebSocket socket)
    {
        Exception? error = null;

        try
        {
            while (true)
            {
                var result = await _application.Input.ReadAsync();
                var buffer = result.Buffer;

                // Get a frame from the application
                try
                {
                    if (result.IsCanceled)
                    {
                        break;
                    }

                    if (!buffer.IsEmpty)
                    {
                        
                        try
                        {
                            _logger.Debug($"Sending payload: {buffer.Length} bytes.");
                            
                            var webSocketMessageType = _connection.ActiveFormat == AspNetTransferFormat.Binary
                                ? WebSocketMessageType.Binary
                                : WebSocketMessageType.Text;

                            if (_options.FramePackets)
                            {
                                while (_frameReader.ReadFrame(ref buffer, out var frame, out var isEndOfMessage))
                                {
                                    if (WebSocketCanSend(socket))
                                    {
                                        await socket.SendAsync(frame, webSocketMessageType, isEndOfMessage);
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }   
                            }
                            else
                            {
                                if (WebSocketCanSend(socket))
                                {
                                    await socket.SendAsync(buffer, webSocketMessageType, true);
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            if (!_aborted)
                            {
                                _logger.Trace("Error writing frame.", ex);
                            }
                            break;
                        }
                    }
                    else if (result.IsCompleted)
                    {
                        break;
                    }
                }
                finally
                {
                    _application.Input.AdvanceTo(buffer.Start, buffer.End);
                }
            }
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            // Send the close frame before calling into user code
            if (WebSocketCanSend(socket))
            {
                try
                {
                    // We're done sending, send the close frame to the client if the websocket is still open
                    await socket.CloseOutputAsync(error != null ? WebSocketCloseStatus.InternalServerError : WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.Trace("Closing webSocket failed.", ex);
                }
            }

            if (_gracefulClose)
            {
                _application.Input.Complete();
            }

            if (error is not null)
            {
                _logger.Trace("Send loop errored.", error);
            }
        }
    }

    private static bool WebSocketCanSend(WebSocket ws)
    {
        return !(ws.State == WebSocketState.Aborted ||
                 ws.State == WebSocketState.Closed ||
                 ws.State == WebSocketState.CloseSent);
    }
}