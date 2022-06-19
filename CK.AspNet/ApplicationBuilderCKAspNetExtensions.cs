using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System;
using Microsoft.AspNetCore.Hosting;
using CK.AspNet;

namespace Microsoft.AspNetCore.Builder
{
    /// <summary>
    /// Adds extension methods on <see cref="IApplicationBuilder"/>.
    /// </summary>
    public static class ApplicationBuilderCKAspNetExtensions
    {
        /// <summary>
        /// Configures the <see cref="RequestGuardMonitorMiddleware"/> that will catch any exceptions from the following
        /// middlewares to the request's <see cref="IActivityMonitor"/>.
        /// Note that <see cref="Microsoft.Extensions.Hosting.HostBuilderMonitoringHostExtensions.UseCKMonitoring(Microsoft.Extensions.Hosting.IHostBuilder)">HostBuilder.UseCKMonitoring</see> must have been
        /// called on the builder.
        /// </summary>
        /// <param name="this">This application builder.</param>
        /// <param name="options">Optional configuration.</param>
        /// <returns>The application builder.</returns>
        public static IApplicationBuilder UseGuardRequestMonitor( this IApplicationBuilder @this, RequestGuardMonitorMiddlewareOptions options = null )
        {
            return @this.UseMiddleware<RequestGuardMonitorMiddleware>( options ?? new RequestGuardMonitorMiddlewareOptions() );
        }

        /// <summary>
        /// Configures the <see cref="RequestGuardMonitorMiddleware"/> that will catch any exceptions from the following
        /// middlewares to the request's <see cref="IActivityMonitor"/>.
        /// Note that <see cref="Microsoft.Extensions.Hosting.HostBuilderMonitoringHostExtensions.UseCKMonitoring(Microsoft.Extensions.Hosting.IHostBuilder)">HostBuilder.UseCKMonitoring</see> must have been
        /// called on the builder.
        /// </summary>
        /// <param name="this">This application builder.</param>
        /// <param name="options">Configuration for options.</param>
        /// <returns>The application builder.</returns>
        public static IApplicationBuilder UseGuardRequestMonitor( this IApplicationBuilder @this, Action<RequestGuardMonitorMiddlewareOptions> options )
        {
            var o = new RequestGuardMonitorMiddlewareOptions();
            options( o );
            return @this.UseMiddleware<RequestGuardMonitorMiddleware>( o );
        }
    }
}
