using CK.Core;
using CK.SqlServer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace CK.AspNet
{
    /// <summary>
    /// Adds extension methods on <see cref="HttpContext"/>.
    /// Since the extension methods here do not conflict with more generic methods, the namespace is
    /// CK.AspNet to avoid cluttering the namespace names.
    /// </summary>
    public static class DBHttpContextExtensions
    {
        /// <summary>
        /// Gets a <see cref="ISqlCallContext"/> from the request.
        /// Allocated 
        /// </summary>
        /// <param name="this"></param>
        /// <returns></returns>
        public static ISqlCallContext GetSqlCallContext( this HttpContext @this )
        {
            object o = @this.Items[typeof( ISqlCallContext )];
            if( o != null ) return (ISqlCallContext)o;
            var c = new SqlStandardCallContext( @this.GetRequestMonitor() );
            @this.Items.Add( typeof( ISqlCallContext ), c );
            @this.Response.RegisterForDispose( c );
            return c;
        }


    }
}
