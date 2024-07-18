using CK.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using System;
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
            // TODO: Build a Topic based on the Request.
            scoped.Setup( ctx, new ActivityMonitor() );

            // TODO: integrate this into the ScopedHttpContext.
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
                        LogError( ctx.RequestServices, null, ex );
                        tcs.SetException( ex );
                    }
                    else
                    {
                        LogCanceled( ctx.RequestServices );
                        tcs.SetCanceled();
                    }
                }, TaskScheduler.Default );
            }
            catch( Exception ex )
            {
                LogError( ctx.RequestServices, "Synchronous error in next middleware.", ex );
                tcs.SetException( ex );
            }
            return tcs.Task;
        }

        static void LogError( IServiceProvider services, string? text, Exception ex )
        {
            if( services.GetService( typeof( IActivityMonitor ) ) is IActivityMonitor monitor )
            {
                monitor.UnfilteredLog( LogLevel.Fatal | LogLevel.IsFiltered, null, text, ex );
                monitor.MonitorEnd( "Request error." );
            }
            else
            {
                IActivityLineEmitter logger = services.GetService( typeof( IParallelLogger ) ) as IActivityLineEmitter ?? ActivityMonitor.StaticLogger;
                logger.UnfilteredLog( LogLevel.Fatal | LogLevel.IsFiltered, null, text, ex );
            }
        }

        static void LogCanceled( IServiceProvider services )
        {
            if( services.GetService( typeof( IActivityMonitor ) ) is IActivityMonitor monitor )
            {
                monitor.MonitorEnd( "Request canceled." );
            }
            else
            {
                IActivityLineEmitter logger = services.GetService( typeof( IParallelLogger ) ) as IActivityLineEmitter ?? ActivityMonitor.StaticLogger;
                logger.UnfilteredLog( LogLevel.Fatal | LogLevel.IsFiltered, null, "Request canceled.", null );
            }
        }
    }
}
