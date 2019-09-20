using CK.Core;
using Microsoft.AspNetCore.Http;
using System;

namespace CK.AspNet
{
    public static class HttpContextCKAspNetExtensions
    {
        [Obsolete(
@"IActivityMonitor is now registered as a scoped service in the DI container.
Please use injection instead of serviceProvider.GetService<IActivityMonitor>()
(this GetRequestMonitor is service locator anti-pattern).", true )]
        public static IActivityMonitor GetRequestMonitor( this HttpContext @this )
        {
            return @this.RequestServices.GetService<IActivityMonitor>( false );
        }


    }
}
