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
        class Entry
        {
            static public object DefaultKey = typeof( ISqlCallContext );

            readonly IActivityMonitor _monitor;
            readonly int _hash;

            public Entry( IActivityMonitor m )
            {
                _monitor = m;
                _hash = DefaultKey.GetHashCode() ^ m.GetHashCode();
            }

            public override bool Equals( object obj )
            {
                Entry e = obj as Entry;
                return e != null && e._monitor == _monitor;
            }

            public override int GetHashCode() => _hash;
        }

        /// <summary>
        /// Gets a <see cref="ISqlCallContext"/> from the context, optionally associated to a specific monitor.
        /// </summary>
        /// <param name="this">This HttpContext.</param>
        /// <param name="monitor">
        /// Optional monitor associated to the <see cref="ISqlCallContext"/>. 
        /// By default the context's one is used (<see cref="HttpContextCKAspNetExtensions.GetRequestMonitor(HttpContext)">HttpContext.GetRequestMonitor()</see>).
        /// </param>
        /// <returns>The ISqlCallContext to associated to the current context.</returns>
        public static ISqlCallContext GetSqlCallContext( this HttpContext @this, IActivityMonitor monitor = null )
        {
            SqlStandardCallContext c;
            // Fast path.
            if( monitor == null )
            {
                object o = @this.Items[Entry.DefaultKey];
                if( o != null ) return (ISqlCallContext)o;
                // Creates the default call context.
                c = new SqlStandardCallContext( @this.GetRequestMonitor() );
                @this.Items.Add( Entry.DefaultKey, c );
                @this.Items.Add( new Entry( c.Monitor ), c );
            }
            else
            {
                Entry e = new Entry( monitor );
                object o = @this.Items[e];
                if( o != null ) return (ISqlCallContext)o;
                c = new SqlStandardCallContext( monitor );
                @this.Items.Add( e, c );
            }
            @this.Response.RegisterForDispose( c );
            return c;
        }

    }
}
