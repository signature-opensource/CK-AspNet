using CK.AspNet;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.AspNetCore.Hosting
{
    public static class WebHostBuilderCKAspNetExtensions
    {
        /// <summary>
        /// Automatically provides a <see cref="ScopedHttpContext"/> service that enables scoped services
        /// to use the HttpContext.
        /// </summary>
        /// <remarks>
        /// This is much more efficient than the HttpContextAccessor. HttpContextAccessor remains the only
        /// way to have a singleton service depends on the HttpContext and must NEVER be used. Singleton
        /// services that MAY need the HttpContxt must be designed with explicit HttpContext method parameter 
        /// injection.
        /// A contrario, Scoped services CAN easily depend on the HttpContext thanks to this ScopedHttpContext.
        /// </remarks>
        /// <param name="builder">This Web host builder.</param>
        /// <returns>The builder.</returns>
        public static IWebHostBuilder UseScopedHttpContext( this IWebHostBuilder builder ) => ScopedHttpContext.Install( builder );
    }
}
