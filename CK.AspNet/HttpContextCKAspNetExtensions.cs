using CK.Core;
using Microsoft.AspNetCore.Http;
using System;

namespace CK.AspNet
{
    /// <summary>
    /// Adds extension methods on <see cref="HttpContext"/>.
    /// Since the extension methods here do not conflict with more generic methods, the namespace is
    /// CK.AspNet to avoid cluttering the namespace names.
    /// </summary>
    public static class HttpContextCKAspNetExtensions
    {

        /// <summary>
        /// Gets the Request monitor setup by <see cref="RequestMonitorMiddleware"/>.
        /// </summary>
        /// <param name="this">This http context.</param>
        /// <returns>The activity monitor.</returns>
        public static IActivityMonitor GetRequestMonitor( this HttpContext @this )
        {
            return (IActivityMonitor)@this.Items[typeof( IActivityMonitor )];
        }


    }
}
