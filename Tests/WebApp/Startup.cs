using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using CK.AspNet;

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
                 if(context.Request.Path.StartsWithSegments( "/bug" ))
                 {
                     throw new Exception( "A bug occurred." );
                 }
                 await context.Response.WriteAsync( "Hello World!" );
             } );
        }
    }
}
