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
    /// logged into the current (scoped) request monitor if it exists.
    /// By default, execution errors are re-thrown.
    /// </summary>
    public sealed class RequestGuardMonitorMiddleware
    {
        readonly RequestDelegate _next;
        readonly bool _swallowErrors;

        /// <summary>
        /// Initializes a new <see cref="RequestGuardMonitorMiddleware"/> with options.
        /// </summary>
        /// <param name="next">Next middleware.</param>
        /// <param name="swallowErrors">True to swallow error instead of re-throwing it (to the preceding middlewares).</param>
        public RequestGuardMonitorMiddleware( RequestDelegate next, bool swallowErrors = false )
        {
            _next = next;
            _swallowErrors = swallowErrors;
        }

        /// <summary>
        /// Invokes the next request handler and logs any error if a <see cref="IActivityMonitor"/> exists
        /// in the <see cref="HttpContext.RequestServices"/>.
        /// </summary>
        /// <param name="ctx">The current context.</param>
        /// <returns>The awaitable.</returns>
        public Task InvokeAsync( HttpContext ctx )
        {
            TaskCompletionSource tcs = new TaskCompletionSource();
            // Try/catch is required to handle any synchronous exception.
            try
            {
                _ = _next.Invoke( ctx ).ContinueWith( t =>
                {
                    if( t.Status == TaskStatus.RanToCompletion )
                    {
                        if( ctx.RequestServices.GetService( typeof( IActivityMonitor ) ) is IActivityMonitor monitor )
                        {
                            monitor.MonitorEnd();
                        }
                        tcs.SetResult();
                    }
                    else if( t.Status == TaskStatus.Faulted )
                    {
                        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        Exception ex;
                        var agg = t.Exception;
                        ex = agg != null
                            ? agg.InnerExceptions.Count == 1 ? agg.InnerExceptions[0] : agg
                            : new CKException( "Null exception on Faulted Task." );
                        if( ctx.RequestServices.GetService( typeof( IActivityMonitor ) ) is IActivityMonitor monitor )
                        {
                            monitor.UnfilteredLog( LogLevel.Fatal, null, null, ex );
                            monitor.MonitorEnd( "Request error." );
                        }
                        tcs.SetException( ex );
                        if( _swallowErrors )
                            tcs.SetResult();
                        else tcs.SetException( t.Exception );
                    }
                    else
                    {
                        if( ctx.RequestServices.GetService( typeof( IActivityMonitor ) ) is IActivityMonitor monitor )
                        {
                            monitor.MonitorEnd( "Canceled request." );
                        }
                        tcs.SetCanceled();
                    }
                }, TaskScheduler.Default );
            }
            catch( Exception ex )
            {
                if( ctx.RequestServices.GetService( typeof( IActivityMonitor ) ) is IActivityMonitor monitor )
                {
                    monitor.UnfilteredLog( LogLevel.Fatal, null, "Synchronous error in next middleware.", ex );
                    monitor.MonitorEnd( "Request error." );
                }
                if( _swallowErrors )
                    tcs.SetResult();
                else tcs.SetException( ex );
            }
            return tcs.Task;
        }
    }
}
