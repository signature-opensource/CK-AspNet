using CK.AspNet;
using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System;

namespace Microsoft.AspNetCore.Builder
{
    /// <summary>
    /// Adds extension methods on <see cref="IApplicationBuilder"/>.
    /// </summary>
    public static class ApplicationBuilderCKAspNetExtensions
    {
        /// <summary>
        /// Configures the <see cref="RequestMonitorMiddleware"/> that will catch any exceptions from the following
        /// middlewares to the request's <see cref="IActivityMonitor"/>
        /// </summary>
        /// <param name="this">This application builder.</param>
        /// <param name="options">Optional configuration.</param>
        /// <returns>The application builder.</returns>
        public static IApplicationBuilder UseRequestMonitor( this IApplicationBuilder @this, RequestMonitorMiddlewareOptions options = null )
        {
            return @this.UseMiddleware<RequestMonitorMiddleware>( options ?? new RequestMonitorMiddlewareOptions() );
        }

        /// <summary>
        /// Configures the <see cref="RequestMonitorMiddleware"/> that will catch any exceptions from the following
        /// middlewares to the request's <see cref="IActivityMonitor"/>
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
