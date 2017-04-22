using CK.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.AspNet
{
    public class RequestMonitorMiddlewareOptions : IOptions<RequestMonitorMiddlewareOptions>
    {
        public Action<HttpContext,IActivityMonitor> OnStartRequest { get; set; }

        public Action<HttpContext, IActivityMonitor> OnEndRequest { get; set; }

        public Action<HttpContext, IActivityMonitor, Exception> OnRequestError { get; set; }

        RequestMonitorMiddlewareOptions IOptions<RequestMonitorMiddlewareOptions>.Value => this;
    }
}
