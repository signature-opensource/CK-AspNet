using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System;

namespace CK.AspNet
{
    /// <summary>
    /// Adds extension methods on <see cref="IApplicationBuilder"/>.
    /// Since the extension methods here do not conflict with more generic methods, the namespace is
    /// CK.AspNet to avoid cluttering the namespace names.
    /// </summary>
    public static class ApplicationBuilderCKAspNetExtensions
    {
        /// <summary>
        /// Configures the <see cref="RequestMonitorMiddleware"/> that will associate a <see cref="IActivityMonitor"/>
        /// to each request.
        /// </summary>
        /// <param name="this">This application builder.</param>
        /// <param name="options">Optional configuration.</param>
        /// <returns>The application builder.</returns>
        public static IApplicationBuilder UseRequestMonitor( this IApplicationBuilder @this, RequestMonitorMiddlewareOptions options = null )
        {
            return options != null
                    ? @this.UseMiddleware<RequestMonitorMiddleware>( options )
                    : @this.UseMiddleware<RequestMonitorMiddleware>();
        }

        /// <summary>
        /// Configures the <see cref="RequestMonitorMiddleware"/> that will associate a <see cref="IActivityMonitor"/>
        /// to each request.
        /// </summary>
        /// <param name="this">This application builder.</param>
        /// <param name="options">Configuration for options.</param>
        /// <returns>The application builder.</returns>
        public static IApplicationBuilder UseRequestMonitor( this IApplicationBuilder @this, Action<RequestMonitorMiddlewareOptions> options )
        {
            var o = new RequestMonitorMiddlewareOptions();
            options( o );
            return @this.UseMiddleware<RequestMonitorMiddleware>( o );
        }
    }
}
