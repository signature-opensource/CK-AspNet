using CK.AspNet;
using CK.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Http
{
    /// <summary>
    /// Provides a service locator for <see cref="IActivityMonitor"/> on <see cref="HttpContext"/>.
    /// Note that no GetRequestMonitor() exists for the <see cref="ScopedHttpContext"/>: the monitor
    /// should not be "located" by scoped services but should be injected in the constructor.
    /// </summary>
    public static class HttpContextCKAspNetExtensions
    {
        /// <summary>
        /// Gets the <see cref="IActivityMonitor"/> from the <see cref="HttpContext.RequestServices"/>.
        /// </summary>
        /// <param name="c">This context.</param>
        /// <returns>The request's monitor.</returns>
        public static IActivityMonitor GetRequestMonitor( this HttpContext c ) => c.RequestServices.GetRequiredService<IActivityMonitor>();
    }

}
