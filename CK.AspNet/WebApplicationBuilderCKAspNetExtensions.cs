using CK.AspNet;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.Hosting
{
    /// <summary>
    /// Provides <see cref="UseScopedHttpContext(WebApplicationBuilder)"/>.
    /// </summary>
    public static class WebApplicationBuilderCKAspNetExtensions
    {
        /// <summary>
        /// Does the same as <see cref="UseScopedHttpContext(WebApplicationBuilder)"/> that should be used instead of this one.
        /// <para>
        /// When changed, make sure that this is no more called.
        /// </para>
        /// </summary>
        /// <param name="builder">This Web host builder.</param>
        /// <returns>The builder.</returns>
        [Obsolete( "Please change your startup code to use the WebApplicationBuilder instead (Minimal API) and don't call both!" )]
        public static IWebHostBuilder UseScopedHttpContext( this IWebHostBuilder builder ) => ScopedHttpContext.Install( builder );

        /// <summary>
        /// Automatically provides a <see cref="ScopedHttpContext"/> service that enables scoped services
        /// to use the HttpContext. This can be called multiple times.
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
        /// <param name="builder">This Web host builder.</param>
        /// <returns>The builder.</returns>
        public static WebApplicationBuilder UseScopedHttpContext( this WebApplicationBuilder builder ) => ScopedHttpContext.Install( builder );
    }
}
