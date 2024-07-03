using CK.AspNet;
using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.AspNetCore.Hosting
{
    /// <summary>
    /// Provides <see cref="UseScopedHttpContext(WebApplicationBuilder)"/>.
    /// </summary>
    public static class WebHostBuilderCKAspNetExtensions
    {
        /// <summary>
        /// Does the same as <see cref="ApplicationBuilderCKAspNetExtensions.AddScopedHttpContext(WebApplicationBuilder)"/> and
        /// <see cref="ApplicationBuilderCKAspNetExtensions.UseScopedHttpContext(IApplicationBuilder)"/> that should be used instead of this one.
        /// <para>
        /// In minimal API mode (ie. when this <paramref name="builder"/> is a <see cref="ConfigureWebHostBuilder"/>), this
        /// throws a <see cref="CK.Core.CKException"/>.
        /// </para>
        /// </summary>
        /// <param name="builder">This Web host builder.</param>
        /// <returns>The builder.</returns>
        [Obsolete( "Please change your startup code to use WebApplication.UseScopedHttpContext() (IApplicationBuilder extension) instead (Minimal API)." )]
        public static IWebHostBuilder UseScopedHttpContext( this IWebHostBuilder builder ) => Install( builder );


        sealed class Middleware
        {
            readonly RequestDelegate _next;

            public Middleware( RequestDelegate next )
            {
                _next = next;
            }

            public async Task InvokeAsync( HttpContext c, ScopedHttpContext p )
            {
                Debug.Assert( p.HttpContext == null );
                p.HttpContext = c;
                await _next( c );
            }
        }

        static readonly string _uniqueKey = typeof( WebHostMiddleWareInstaller ).FullName;

        sealed class WebHostMiddleWareInstaller : IStartupFilter
        {
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

        static IWebHostBuilder Install( IWebHostBuilder builder )
        {
            if( builder is ConfigureWebHostBuilder )
            {
                Throw.CKException( "When WebApplicationBuilder is used, the UseScopedHttpContext() must be called directly on the WebApplicationBuilder." );
            }
            return builder.ConfigureServices( ( ctx, services ) =>
            {
                services.AddTransient<IStartupFilter>( _ => new WebHostMiddleWareInstaller() )
                        .TryAddScoped<ScopedHttpContext>();
            } );
        }



    }
}
