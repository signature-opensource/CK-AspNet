using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace CK.AspNet
{
    /// <summary>
    /// Provides the <see cref="HttpContext"/> as a scoped dependency.
    /// This must be installed thanks to <see cref="WebHostBuilderCKAspNetExtensions.UseScopedHttpContext(IWebHostBuilder)"/>
    /// extension method.
    /// </summary>
    [ContainerConfiguredScopedService]
    public sealed class ScopedHttpContext
    {
        /// <summary>
        /// Gets the current HttpContext of the request.
        /// </summary>
        public HttpContext HttpContext { get; internal set; }

    }
}
