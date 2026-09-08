using System;
using AspNetTransferFormat = Microsoft.AspNetCore.Connections.TransferFormat;
using CK.Core;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
#if NET8_0_OR_GREATER
using Microsoft.AspNetCore.Http.Timeouts;
#endif

namespace CK.AspNet.WebSocket;

internal class WebSocketConnectionContext : ConnectionContext,
    IConnectionIdFeature,
    IConnectionItemsFeature,
    IConnectionTransportFeature,
    IConnectionUserFeature,
    ITransferFormatFeature,
    IHttpTransportFeature,
    IConnectionLifetimeFeature
{
    private readonly HttpContext _httpContext;
    private readonly object _itemsLock = new();
    private readonly object _stateLock = new();
    private bool _disposed;
    private IDictionary<object, object?>? _items;
    private readonly TaskCompletionSource _disposeTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _connectionClosedTokenSource;

    internal WebSocketConnectionContext(string id, HttpContext httpContext, IDuplexPipe transport, IDuplexPipe application,
        WebSocketConnectionDispatcherOptions options)
    {
        ConnectionId = id;
        Application = application;
        Transport = transport;
        Options = options;

        // The default behavior is that both formats are supported.
        SupportedFormats = AspNetTransferFormat.Text | AspNetTransferFormat.Binary;
        ActiveFormat = options.WebSockets.TransferFormat == TransferFormat.Binary
            ? AspNetTransferFormat.Binary
            : AspNetTransferFormat.Text;

        _connectionClosedTokenSource = new CancellationTokenSource();
        ConnectionClosed = _connectionClosedTokenSource.Token;

        _httpContext = httpContext;

        Features = new FeatureCollection();
        Features.Set<IConnectionUserFeature>(this);
        Features.Set<IConnectionItemsFeature>(this);
        Features.Set<IConnectionIdFeature>(this);
        Features.Set<IConnectionTransportFeature>(this);
        Features.Set<ITransferFormatFeature>(this);
        Features.Set<IHttpTransportFeature>(this);
        Features.Set<IConnectionLifetimeFeature>(this);
    }

    public override string ConnectionId { get; set; }
    public IDuplexPipe Application { get; }
    public WebSocketConnectionDispatcherOptions Options { get; }
    public override IFeatureCollection Features { get; }
    internal Task? TransportTask { get; private set; }
    internal Task? ApplicationTask { get; private set; }

    public override IDictionary<object, object?> Items
    {
        get
        {
            if (_items == null)
            {
                lock (_itemsLock)
                {
                    if (_items == null)
                    {
                        _items = new ConnectionItems(new ConcurrentDictionary<object, object?>());
                    }
                }
            }

            return _items;
        }
        set => _items = value ?? throw new ArgumentNullException(nameof(value));
    }

    public ClaimsPrincipal? User
    {
        get => _httpContext.User;
        set
        {

        }
    }
    public AspNetTransferFormat SupportedFormats { get; set; }
    public AspNetTransferFormat ActiveFormat { get; set; }
    public HttpTransportType TransportType => HttpTransportType.WebSockets;
    public override IDuplexPipe Transport { get; set; }
    public CancellationTokenSource? Cancellation { get; set; }

    internal bool TryActivateConnection(
        IActivityMonitor monitor,
        Func<IActivityMonitor, ConnectionContext, Task> connectionDelegate,
        IHttpTransport transport,
        HttpContext context)
    {
        // Call into the end point passing the connection
        ApplicationTask = ExecuteApplication(monitor, connectionDelegate);

        // Start the transport
        TransportTask = transport.ProcessRequestAsync(monitor.CreateToken(), context, context.RequestAborted);
#if NET8_0_OR_GREATER
        context.Features.Get<IHttpRequestTimeoutFeature>()?.DisableTimeout();
#endif
        return true;
    }

    private async Task ExecuteApplication(IActivityMonitor monitor, Func<IActivityMonitor, ConnectionContext, Task> connectionDelegate)
    {
        // Jump onto the thread pool thread so blocking user code doesn't block the setup of the
        // connection and transport
        await Task.Yield();

        // Running this in an async method turns sync exceptions into async ones
        await connectionDelegate(monitor, this);
    }

    public override void Abort(ConnectionAbortedException abortReason)
        => ThreadPool.UnsafeQueueUserWorkItem(cts => ((CancellationTokenSource)cts!).Cancel(),
            _connectionClosedTokenSource);

    public async Task DisposeAsync(bool closeGracefully = false)
    {
        Task disposeTask;

        try
        {
            lock (_stateLock)
            {
                if (_disposed)
                {
                    disposeTask = _disposeTcs.Task;
                }
                else
                {
                    _disposed = true;

                    ActivityMonitor.StaticLogger.Debug($"Disposing connection {ConnectionId}.");

                    var applicationTask = ApplicationTask ?? Task.CompletedTask;
                    var transportTask = TransportTask ?? Task.CompletedTask;

                    disposeTask = WaitOnTasks(applicationTask, transportTask, closeGracefully);
                }
            }
        }
        finally
        {
            Cancellation?.Dispose();
            Cancellation = null;
        }

        await disposeTask;
    }

    private async Task WaitOnTasks(Task applicationTask, Task transportTask, bool closeGracefully)
    {
        try
        {
            // Closing gracefully means we're only going to close the finished sides of the pipe
            // If the application finishes, that means it's done with the transport pipe
            // If the transport finishes, that means it's done with the application pipe
            if (!closeGracefully)
            {
                Application?.Output.CancelPendingFlush();

                // The websocket transport will close the application output automatically when reading is canceled
                // ReSharper disable once MethodHasAsyncOverload
                Cancellation?.Cancel();
            }

            // Wait for either to finish
            var result = await Task.WhenAny(applicationTask, transportTask);

            // If the application is complete, complete the transport pipe (it's the pipe to the transport)
            if (result == applicationTask)
            {
                if( Transport is not null )
                {
                    await Transport.Output.CompleteAsync(applicationTask.Exception?.InnerException);
                    await Transport.Input.CompleteAsync();
                }

                try
                {
                    ActivityMonitor.StaticLogger.Debug($"Waiting for {TransportType} transport to complete on connection '{ConnectionId}'.");

                    // Transports are written by us and are well behaved, wait for them to drain
                    await transportTask;
                }
                finally
                {
                    ActivityMonitor.StaticLogger.Debug($"{TransportType} transport complete on connection '{ConnectionId}'.");

                    // Now complete the application
                    if( Application is not null )
                    {
                        await Application.Output.CompleteAsync();
                        await Application.Input.CompleteAsync();
                    }

                    // Trigger ConnectionClosed
                    ThreadPool.UnsafeQueueUserWorkItem(cts => ((CancellationTokenSource)cts!).Cancel(),
                        _connectionClosedTokenSource);
                }
            }
            else
            {
                // If the transport is complete, complete the application pipes
                if( Application is not null )
                {
                    await Application.Output.CompleteAsync(transportTask.Exception?.InnerException);
                    await Application.Input.CompleteAsync();
                }

                // Trigger ConnectionClosed
                ThreadPool.UnsafeQueueUserWorkItem(cts => ((CancellationTokenSource)cts!).Cancel(),
                    _connectionClosedTokenSource);

                try
                {
                    // A poorly written application *could* in theory get stuck forever and it'll show up as a memory leak
                    ActivityMonitor.StaticLogger.Debug($"Waiting for application to complete on connection '{ConnectionId}'.");

                    await applicationTask;
                }
                finally
                {
                    ActivityMonitor.StaticLogger.Debug($"Application complete on connection '{ConnectionId}'.");

                    if( Transport is not null )
                    {
                        await Transport.Output.CompleteAsync();
                        await Transport.Input.CompleteAsync();
                    }
                }
            }

            // Notify all waiters that we're done disposing
            _disposeTcs.TrySetResult();
        }
        catch (OperationCanceledException)
        {
            _disposeTcs.TrySetCanceled();

            throw;
        }
        catch (Exception ex)
        {
            _disposeTcs.TrySetException(ex);

            throw;
        }
    }
}
