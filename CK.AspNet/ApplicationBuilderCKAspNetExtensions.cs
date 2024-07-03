using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using System;
using Microsoft.AspNetCore.Hosting;
using CK.AspNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Builder
{
    /// <summary>
    /// Adds extension methods on <see cref="WebApplicationBuilder"/> and <see cref="IApplicationBuilder"/>.
    /// </summary>
    public static class ApplicationBuilderCKAspNetExtensions
    {
        /// <summary>
        /// Registers the <see cref="ScopedHttpContext"/> in the <see cref="WebApplicationBuilder.Services"/>.
        /// <para>
        /// This can be called multiple times.
        /// </para>
        /// <para>
        /// <see cref="UseScopedHttpContext(IApplicationBuilder)"/> MUST be called on the <see cref="IApplicationBuilder"/>
        /// otherwise this scoped will not be initialized.
        /// </para>
        /// </summary>
        /// <param name="builder">This builder.</param>
        /// <returns>This builder.</returns>
        public static WebApplicationBuilder AddScopedHttpContext( this WebApplicationBuilder builder )
        {
            // Use the (deprecated) Host.Properties dictionary (O(1)) instead of TryAddScoped (O(n)).
            if( builder.Host.Properties.TryAdd( typeof( ScopedHttpContext ), null ) )
            {
                builder.Services.AddScoped<ScopedHttpContext>();
            }
            return builder;
        }

        /// <summary>
        /// Configures the <see cref="ScopedHttpContext"/> service that enables scoped services
        /// to use the HttpContext. <see cref="AddScopedHttpContext(WebApplicationBuilder)"/> MUST
        /// have been called.
        /// <para>
        /// This can be called multiple times.
        /// </para>
        /// </summary>
        /// <remarks>
        /// This is much more efficient than the HttpContextAccessor. HttpContextAccessor remains the only
        /// way to have a singleton service depends on the HttpContext and must NEVER be used. Singleton
        /// services that MAY need the HttpContext must be designed with explicit HttpContext method parameter 
        /// injection.
        /// <para>
        /// Scoped services however CAN easily depend on the HttpContext thanks to this ScopedHttpContext.
        /// </para>
        /// </remarks>
        /// <param name="builder">This <see cref="IApplicationBuilder"/>.</param>
        /// <returns>The builder.</returns>
        public static IApplicationBuilder UseScopedHttpContext( this IApplicationBuilder builder )
        {
            if( builder.Properties.TryAdd( nameof( ScopedHttpContextMiddleware ), nameof( ScopedHttpContextMiddleware ) ) )
            {
                builder.UseMiddleware<ScopedHttpContextMiddleware>();
            }
            return builder;
        }

        /// <summary>
        /// Configures the <see cref="RequestGuardMonitorMiddleware"/> that will catch any exceptions from the following
        /// middlewares to the request's <see cref="IActivityMonitor"/> if it exists. If no monitor is available in the 
        /// <see cref="HttpContext.RequestServices"/>, this middleware does nothing.
        /// <para>
        /// Note that an <see cref="OperationCanceledException"/> is not swallowed. There is no HTTP status code for this,
        /// server side cancellations because of timeout or any other reasons should be managed by higher level protocols.
        /// </para>
        /// <para>
        /// This can be called multiple times.
        /// </para>
        /// </summary>
        /// <param name="builder">This application builder.</param>
        /// <param name="swallowErrors">True to swallow error instead of re-throwing it (to the preceding middlewares).</param>
        /// <returns>The application builder.</returns>
        public static IApplicationBuilder UseGuardRequestMonitor( this IApplicationBuilder builder, bool swallowErrors = false )
        {
            if( builder.Properties.TryAdd( nameof( RequestGuardMonitorMiddleware ), nameof( RequestGuardMonitorMiddleware ) ) )
            {
                builder.UseMiddleware<RequestGuardMonitorMiddleware>( swallowErrors );
            }
            return builder;
        }

    }

}
