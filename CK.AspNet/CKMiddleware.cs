using CK.Core;
using Microsoft.AspNetCore.Http;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace CK.AspNet
{
    /// <summary>
    /// Configures the ScopedHttpContext and acts as an error guard in middleware pipeline: any exceptions
    /// raised by next middlewares are logged into the current (scoped) request monitor if it exists.
    /// </summary>
    sealed class CKMiddleware
    {
        readonly RequestDelegate _next;

        public CKMiddleware( RequestDelegate next )
        {
            _next = next;
        }

        public Task InvokeAsync( HttpContext ctx, ScopedHttpContext scoped )
        {
            Throw.DebugAssert( scoped.HttpContext is null );
            scoped.HttpContext = ctx;
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
                            monitor.UnfilteredLog( LogLevel.Fatal | LogLevel.IsFiltered, null, null, ex );
                            monitor.MonitorEnd( "Request error." );
                        }
                        tcs.SetException( ex );
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
                    monitor.UnfilteredLog( LogLevel.Fatal | LogLevel.IsFiltered, null, "Synchronous error in next middleware.", ex );
                    monitor.MonitorEnd( "Request error." );
                }
                tcs.SetException( ex );
            }
            return tcs.Task;
        }
    }

}
