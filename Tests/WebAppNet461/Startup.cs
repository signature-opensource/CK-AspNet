using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using CK.AspNet;
using Microsoft.Extensions.Logging;

namespace WebApp
{
    public class Startup
    {
        public void ConfigureServices( IServiceCollection services )
        {
        }

        public void Configure( IApplicationBuilder app, IHostingEnvironment env )
        {
            if( env.IsDevelopment() )
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseRequestMonitor();

            app.Run( async ( context ) =>
            {
                if( context.Request.Path.StartsWithSegments( "/bug" ) )
                {
                    throw new Exception( "A bug occurred." );
                }
                await context.Response.WriteAsync( "Hello World!" );
                var f = context.RequestServices.GetService<ILoggerFactory>();
                var logger = f.CreateLogger( "Test" );
                logger.LogCritical( $"This is a Critical log." );
                logger.LogError( $"This is a Error log." );
                logger.LogWarning( $"This is a Warning log." );
                logger.LogInformation( $"This is a Information log." );
                logger.LogDebug( $"This is a Debug log." );
                logger.LogTrace( $"This is a Trace log." );
            } );
        }
    }
}
