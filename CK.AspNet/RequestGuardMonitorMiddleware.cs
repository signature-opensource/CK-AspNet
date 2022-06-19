using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CK.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace CK.AspNet
{
    /// <summary>
    /// Acts as an error guard in middleware pipeline: any exceptions raised by next middlewares are
    /// logged into the current (scoped) request monitor that is installed by <see cref="Microsoft.Extensions.Hosting.HostBuilderMonitoringHostExtensions.UseCKMonitoring(Microsoft.Extensions.Hosting.IHostBuilder)">HostBuilder.UseCKMonitoring</see>.
    /// extension method.
    /// By default, execution errors are not swallowed but this can be changed thanks to the <see cref="RequestGuardMonitorMiddlewareOptions.SwallowErrors"/> option.
    /// </summary>
    public sealed class RequestGuardMonitorMiddleware
    {
        readonly RequestDelegate _next;
        readonly RequestGuardMonitorMiddlewareOptions _options;
        readonly Action<HttpContext, IActivityMonitor> _onStartRequest;
        readonly Action<HttpContext, IActivityMonitor, TaskStatus> _onEndRequest;
        readonly Action<HttpContext, IActivityMonitor, Exception> _onRequestError;

        /// <summary>
        /// Initializes a new <see cref="RequestGuardMonitorMiddleware"/> with options.
        /// </summary>
        /// <param name="next">Next middleware.</param>
        /// <param name="options">Options.</param>
        public RequestGuardMonitorMiddleware( RequestDelegate next, RequestGuardMonitorMiddlewareOptions options )
        {
            _next = next;
            _options = options;
            _onStartRequest = _options.OnStartRequest ?? DefaultOnStartRequest;
            _onEndRequest = _options.OnEndRequest ?? DefaultOnEndRequest;
            _onRequestError = _options.OnRequestError ?? DefaultOnRequestError;
        }

        /// <summary>
        /// Invokes the next request handler and logs any error.
        /// The exception is rethrown or swallowed depending on <see cref="RequestGuardMonitorMiddlewareOptions.SwallowErrors"/>.
        /// </summary>
        /// <param name="ctx">The current context.</param>
        /// <param name="m">The request scoped monitor.</param>
        /// <returns>The awaitable.</returns>
        public Task InvokeAsync( HttpContext ctx, IActivityMonitor m )
        {
            _onStartRequest.Invoke( ctx, m );
            // There is no non generic TaskCompletionSource.
            TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
            // Try/catch is required to handle any synchronous exception.
            try
            {
                _ = _next.Invoke( ctx ).ContinueWith( t =>
                {
                    if( t.Status == TaskStatus.RanToCompletion )
                    {
                        _onEndRequest.Invoke( ctx, m, t.Status );
                        tcs.SetResult( true );
                    }
                    else if( t.Status == TaskStatus.Faulted )
                    {
                        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        var e = t.Exception;
                        if( e.InnerExceptions.Count == 1 )
                            _onRequestError( ctx, m, e.InnerException );
                        else _onRequestError( ctx, m, e );
                        _onEndRequest.Invoke( ctx, m, t.Status );
                        if( _options.SwallowErrors )
                            tcs.SetResult( null );
                        else tcs.SetException( t.Exception );
                    }
                    else
                    {
                        _onEndRequest.Invoke( ctx, m, t.Status );
                        tcs.SetCanceled();
                    }
                }, TaskScheduler.Default );
            }
            catch( Exception ex )
            {
                _onRequestError( ctx, m, ex );
                _onEndRequest.Invoke( ctx, m, TaskStatus.Faulted );
                if( _options.SwallowErrors )
                    tcs.SetResult( null );
                else tcs.SetException( ex );
            }
            return tcs.Task;
        }

        static void DefaultOnRequestError( HttpContext ctx, IActivityMonitor m, Exception ex )
        {
            m.UnfilteredLog( LogLevel.Fatal, null, null, ex );
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }

        static void DefaultOnStartRequest( HttpContext ctx, IActivityMonitor m )
        {
        }

        static void DefaultOnEndRequest( HttpContext ctx, IActivityMonitor m, TaskStatus status )
        {
            m.MonitorEnd();
        }
    }
}
