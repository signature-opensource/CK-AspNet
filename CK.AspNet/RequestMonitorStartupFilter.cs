using Microsoft.AspNetCore.Hosting;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;

namespace CK.AspNet
{
    class RequestMonitorStartupFilter : IStartupFilter
    {
        private readonly IHostingEnvironment _environment;
        readonly IOptionsMonitor<RequestMonitorMiddlewareOptions> _options;
        public RequestMonitorStartupFilter( IHostingEnvironment environment, IOptionsMonitor<RequestMonitorMiddlewareOptions> options )
        {
            _environment = environment;
            _options = options;
        }

        public Action<IApplicationBuilder> Configure( Action<IApplicationBuilder> next )
        {
            return builder =>
            {
                if( _options.CurrentValue.AutoInsertMiddlewares )
                {
                    if( _environment.IsDevelopment() )
                    {
                        builder.UseDeveloperExceptionPage();
                    }
                    builder.UseRequestMonitor( _options );
                }
                next( builder );
            };
        }
    }
}
