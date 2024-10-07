using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace CK.AspNet;

/// <summary>
/// Provides the <see cref="HttpContext"/> as a scoped dependency.
/// This is installed by <see cref="ApplicationBuilderCKAspNetExtensions.CKBuild(WebApplicationBuilder)"/>
/// extension method and rely on the <see cref="CKMiddleware"/> that must be registered.
/// <para>
/// The <see cref="Monitor"/> is guaranteed to exist even if the <see cref="CKMiddleware"/> has not been called yet.
/// </para>
/// </summary>
[ContainerConfiguredScopedService]
public sealed class ScopedHttpContext
{
    [AllowNull] HttpContext _httpContext;
    IActivityMonitor? _monitor;

    internal void Setup( HttpContext ctx )
    {
        _httpContext = ctx;
        if( _monitor == null )
        {
            // TODO: Build a Topic based on the Request.
            _monitor = new ActivityMonitor();
        }
    }

    /// <summary>
    /// Gets the current HttpContext of the request.
    /// </summary>
    public HttpContext HttpContext => _httpContext;

    /// <summary>
    /// Gets the request monitor.
    /// </summary>
    public IActivityMonitor Monitor => _monitor ??= new ActivityMonitor();


    internal bool HasMonitor => _monitor != null;

}
