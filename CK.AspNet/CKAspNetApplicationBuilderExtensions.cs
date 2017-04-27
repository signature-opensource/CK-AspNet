using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;

namespace CK.AspNet
{
    /// <summary>
    /// Adds extension methods on <see cref="IApplicationBuilder"/>.
    /// Since the extension methods here do not conflict with more generic methods, the namespace is
    /// CK.AspNet to avoid cluttering the namespace names.
    /// </summary>
    public static class CKAspNetApplicationBuilderExtensions
    {
        /// <summary>
        /// Configures the <see cref="RequestMonitorMiddleware"/> that will associate a <see cref="IActivityMonitor"/>
        /// to each request.
        /// </summary>
        /// <param name="this">This application builder.</param>
        /// <param name="options">Options.</param>
        /// <returns>The application builder.</returns>
        public static IApplicationBuilder UseRequestMonitor(this IApplicationBuilder @this, RequestMonitorMiddlewareOptions options)
        {
            return @this.UseMiddleware<RequestMonitorMiddleware>(options);
        }


    }
}
