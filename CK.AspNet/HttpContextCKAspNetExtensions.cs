using CK.AspNet;
using CK.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Http;

/// <summary>
/// Provides a service locator for <see cref="IActivityMonitor"/> on <see cref="HttpContext"/>.
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
