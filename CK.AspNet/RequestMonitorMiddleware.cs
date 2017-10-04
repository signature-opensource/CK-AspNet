using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CK.Core;
using Microsoft.Extensions.Options;

namespace CK.AspNet
{
    /// <summary>
    /// Handles monitor creation associated to the context.
    /// </summary>
    public class RequestMonitorMiddleware
    {
        readonly RequestDelegate _next;
        readonly RequestMonitorMiddlewareOptions _options;
        readonly Action<HttpContext, IActivityMonitor> _onStartRequest;
        readonly Action<HttpContext, IActivityMonitor, TaskStatus> _onEndRequest;
        readonly Action<HttpContext, IActivityMonitor, Exception> _onRequestError;

        /// <summary>
        /// Initializes a new <see cref="RequestMonitorMiddleware"/> with options.
        /// </summary>
        /// <param name="next">Next middleware.</param>
        /// <param name="options">Options.</param>
        public RequestMonitorMiddleware( RequestDelegate next, RequestMonitorMiddlewareOptions options )
        {
            _next = next;
            _options = options;
            _onStartRequest = _options.OnStartRequest ?? DefaultOnStartRequest;
            _onEndRequest = _options.OnEndRequest ?? DefaultOnEndRequest;
            _onRequestError = _options.OnRequestError ?? DefaultOnRequestError;
        }

        /// <summary>
        /// Creates and configures the monitor, invokes the next request handler 
        /// and handles error or cancelation.
        /// </summary>
        /// <param name="ctx">The current context.</param>
        /// <returns>The awaitable.</returns>
        public Task Invoke( HttpContext ctx )
        {
            IActivityMonitor m = new ActivityMonitor();
            ctx.Items.Add( typeof( IActivityMonitor ), m );
            _onStartRequest.Invoke( ctx, m );
            // There is no non generic TaskCompletionSource.
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            // Try/catch is required to handle any synchronous exception.
            try
            {
                _next.Invoke( ctx ).ContinueWith( t =>
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
                            tcs.SetResult( false );
                        else tcs.SetException( t.Exception );
                    }
                    else
                    {
                        _onEndRequest.Invoke( ctx, m, t.Status );
                        tcs.SetCanceled();
                    }
                } );
            }
            catch( Exception ex )
            {
                _onRequestError( ctx, m, ex );
                _onEndRequest.Invoke( ctx, m, TaskStatus.Faulted );
                if( _options.SwallowErrors )
                    tcs.SetResult( false );
                else tcs.SetException( ex );
            }
            return tcs.Task;
        }

        static void DefaultOnRequestError( HttpContext ctx, IActivityMonitor m, Exception ex )
        {
            m.UnfilteredLog( null, LogLevel.Fatal, ex.Message, m.NextLogTime(), ex );
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
