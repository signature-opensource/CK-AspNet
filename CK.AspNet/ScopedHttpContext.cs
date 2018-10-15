using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace CK.AspNet
{
    /// <summary>
    /// Provides the <see cref="HttpContext"/> as a scoped dependency.
    /// This must be installed thanks to <see cref="WebHostBuilderCKAspNetExtensions.UseScopedHttpContext(IWebHostBuilder)"/>
    /// extension method.
    /// </summary>
    public class ScopedHttpContext
    {
        /// <summary>
        /// Gets the current HttpContext of the request.
        /// </summary>
        public HttpContext HttpContext { get; internal set; }

        class Middleware
        {
            readonly RequestDelegate _next;

            public Middleware( RequestDelegate next )
            {
                _next = next;
            }

            public Task Invoke( HttpContext c, ScopedHttpContext p )
            {
                p.HttpContext = c;
                return _next.Invoke( c );
            }
        }

        class MiddleWareInstaller : IStartupFilter
        {
            public Action<IApplicationBuilder> Configure( Action<IApplicationBuilder> next )
            {
                return builder =>
                {
                    builder.UseMiddleware<Middleware>();
                    next( builder );
                };
            }
        }

        internal static IWebHostBuilder Install( IWebHostBuilder builder )
        {

            return builder.ConfigureServices( services =>
            {
                services.AddTransient<IStartupFilter>( _ => new MiddleWareInstaller() )
                        .AddScoped<ScopedHttpContext>();
            } );
        }

    }
}
