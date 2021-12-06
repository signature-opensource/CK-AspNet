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
    /// This must be installed thanks to <see cref="WebHostBuilderCKAspNetExtensions.UseScopedHttpContext(IHostBuilder)"/>
    /// extension method.
    /// </summary>
    public sealed class ScopedHttpContext
    {
        /// <summary>
        /// Gets the current HttpContext of the request.
        /// </summary>
        public HttpContext HttpContext { get; internal set; }

        sealed class Middleware
        {
            readonly RequestDelegate _next;

            public Middleware( RequestDelegate next )
            {
                _next = next;
            }

            public Task InvokeAsync( HttpContext c, ScopedHttpContext p )
            {
                Debug.Assert( p.HttpContext == null );
                p.HttpContext = c;
                return _next.Invoke( c );
            }
        }

        sealed class MiddleWareInstaller : IStartupFilter
        {
            static readonly string _uniqueKey = typeof( MiddleWareInstaller ).FullName;

            public Action<IApplicationBuilder> Configure( Action<IApplicationBuilder> next )
            {
                return builder =>
                {
                    if( !builder.Properties.ContainsKey( _uniqueKey ) )
                    {
                        builder.Properties.Add( _uniqueKey, null );
                        builder.UseMiddleware<Middleware>();
                    }
                    next( builder );
                };
            }
        }

        internal static IWebHostBuilder Install( IWebHostBuilder builder )
        {
            return builder.ConfigureServices( ( ctx, services ) =>
            {
                services.AddTransient<IStartupFilter>( _ => new MiddleWareInstaller() )
                        .TryAddScoped<ScopedHttpContext>();
            } );
        }

    }
}
