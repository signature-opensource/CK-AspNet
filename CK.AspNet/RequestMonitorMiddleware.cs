using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CK.Core;

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
        readonly Action<HttpContext, IActivityMonitor, bool> _onEndRequest;
        readonly Action<HttpContext, IActivityMonitor, Exception> _onRequestError;

        /// <summary>
        /// Initializes a new <see cref="RequestMonitorMiddleware"/>.
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
        /// Creates and configures the monitor.
        /// </summary>
        /// <param name="ctx"></param>
        /// <returns></returns>
        public Task Invoke( HttpContext ctx )
        {
            IActivityMonitor m = new ActivityMonitor();
            ctx.Items.Add(typeof(IActivityMonitor), m);
            bool error = false;
            _onStartRequest.Invoke(ctx, m);
            try
            {
                return _next.Invoke( ctx );
            }
            catch( Exception ex )
            {
                _onRequestError(ctx, m, ex);
                error = true;
                if( _options.RethrowError ) throw;
                // Should be Task.CompletedTask (but not available on NET451).
                return Task.FromResult(0);
            }
            finally
            {
                _onEndRequest.Invoke(ctx, m, error);
            }
        }

        void DefaultOnRequestError(HttpContext ctx, IActivityMonitor m, Exception ex)
        {
            m.UnfilteredLog(null, LogLevel.Error, ex.Message, m.NextLogTime(), ex);
        }

        void DefaultOnStartRequest(HttpContext ctx, IActivityMonitor m)
        {
            m.UnfilteredOpenGroup(null, LogLevel.Info, null, ctx.Request.QueryString.ToString(), m.NextLogTime(), null);
        }

        void DefaultOnEndRequest(HttpContext ctx, IActivityMonitor m, bool onError)
        {
            m.MonitorEnd();
        }
    }
}
