using CK.AspNet;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.Hosting
{
    /// <summary>
    /// Provides <see cref="UseScopedHttpContext(IWebHostBuilder)"/> on web host.
    /// </summary>
    public static class WebHostBuilderCKAspNetExtensions
    {
        /// <summary>
        /// Automatically provides a <see cref="ScopedHttpContext"/> service that enables scoped services
        /// to use the HttpContext.
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
        public static IWebHostBuilder UseScopedHttpContext( this IWebHostBuilder builder ) => ScopedHttpContext.Install( builder );
    }
}
