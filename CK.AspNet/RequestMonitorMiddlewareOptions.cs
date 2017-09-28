using CK.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace CK.AspNet
{
    /// <summary>
    /// Options for the <see cref="RequestMonitorMiddleware"/>.
    /// </summary>
    public class RequestMonitorMiddlewareOptions
    {
        /// <summary>
        /// Gets or sets a replacement of the request start action.
        /// By default, a <see cref="CK.Core.LogLevel.Info"/> with the request string is logged.
        /// </summary>
        public Action<HttpContext,IActivityMonitor> OnStartRequest { get; set; }

        /// <summary>
        /// Gets or sets a replacement of the request end action.
        /// The TaskStatus argument is either <see cref="TaskStatus.RanToCompletion"/>, <see cref="TaskStatus.Canceled"/> 
        /// or <see cref="TaskStatus.Faulted"/> if an error occurred (<see cref="OnRequestError"/> has been called with 
        /// the exception).
        /// By default, <see cref="ActivityMonitorExtension.MonitorEnd"/> is called on the the request monitor.
        /// </summary>
        public Action<HttpContext, IActivityMonitor,TaskStatus> OnEndRequest { get; set; }

        /// <summary>
        /// Gets or sets a replacement of the request error action.
        /// By default, exceptions triggered by the next middlewares in the pipeline are logged as 
        /// <see cref="CK.Core.LogLevel.Fatal"/> errors in the monitor and the response Http status 
        /// is set to <see cref="StatusCodes.Status500InternalServerError"/>.
        /// </summary>
        public Action<HttpContext, IActivityMonitor, Exception> OnRequestError { get; set; }

        /// <summary>
        /// Gets or sets whether exceptions triggered by the next middlewares in the pipeline
        /// must be thrown or only handled by <see cref="OnRequestError"/>.
        /// Defaults to false: middlewares registered before the <see cref="RequestMonitorMiddleware"/> will
        /// by default also receive exceptions.
        /// </summary>
        public bool SwallowErrors { get; set; }

    }
}
