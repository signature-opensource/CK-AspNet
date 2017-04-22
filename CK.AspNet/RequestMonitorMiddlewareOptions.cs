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
        /// </summary>
        public Action<HttpContext,IActivityMonitor> OnStartRequest { get; set; }

        /// <summary>
        /// Gets or sets a replacement of the request end action.
        /// </summary>
        public Action<HttpContext, IActivityMonitor> OnEndRequest { get; set; }

        /// <summary>
        /// Gets or sets a replacement of the request error action.
        /// </summary>
        public Action<HttpContext, IActivityMonitor, Exception> OnRequestError { get; set; }

        RequestMonitorMiddlewareOptions IOptions<RequestMonitorMiddlewareOptions>.Value => this;
    }
}
