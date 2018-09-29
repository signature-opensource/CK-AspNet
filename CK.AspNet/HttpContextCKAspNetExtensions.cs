using CK.Core;
using Microsoft.AspNetCore.Http;
using System;

namespace CK.AspNet
{
    public static class HttpContextCKAspNetExtensions
    {
        [Obsolete(
@"IActivityMonitor is registered as a scoped service in the DI container.
Please try to use injection instead of serviceProvider.GetService<IActivityMonitor>()
(that is service locator anti-pattern).", false )]
        public static IActivityMonitor GetRequestMonitor( this HttpContext @this )
        {
            return @this.RequestServices.GetService<IActivityMonitor>();
        }


    }
}
