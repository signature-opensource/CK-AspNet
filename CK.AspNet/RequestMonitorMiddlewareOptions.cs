using CK.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.AspNet
{
    /// <summary>
    /// Options for the <see cref="RequestMonitorMiddleware"/>.
    /// </summary>
    public class RequestMonitorMiddlewareOptions : IOptions<RequestMonitorMiddlewareOptions>
    {
        /// <summary>
        /// Gets or sets a replacement of the request start action.
        /// By default, a <see cref="LogLevel.Info"/> with the request string is logged.
        /// </summary>
        public Action<HttpContext,IActivityMonitor> OnStartRequest { get; set; }

        /// <summary>
        /// Gets or sets a replacement of the request end action.
        /// The last boolean argument is true if an error occurred (<see cref="OnRequestError"/> has been called with 
        /// the exception).
        /// By default, <see cref="ActivityMonitorExtension.MonitorEnd"/> is called on the the request monitor.
        /// </summary>
        public Action<HttpContext, IActivityMonitor,bool> OnEndRequest { get; set; }

        /// <summary>
        /// Gets or sets a replacement of the request error action.
        /// By default, exceptions triggered by the next middlewares in the pipeline are logged as 
        /// errors in the monitor.
        /// </summary>
        public Action<HttpContext, IActivityMonitor, Exception> OnRequestError { get; set; }


        /// <summary>
        /// Gets or sets whether exceptions triggered by the next middlewares in the pipeline
        /// must be thrown or only handled by <see cref="OnRequestError"/>.
        /// Defaults to false.
        /// </summary>
        public bool RethrowError { get; set; }

        RequestMonitorMiddlewareOptions IOptions<RequestMonitorMiddlewareOptions>.Value => this;
    }
}
