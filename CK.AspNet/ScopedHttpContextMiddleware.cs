using CK.Core;
using Microsoft.AspNetCore.Http;
using CK.AspNet;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Builder
{
    sealed class ScopedHttpContextMiddleware
    {
        readonly RequestDelegate _next;

        public ScopedHttpContextMiddleware( RequestDelegate next )
        {
            _next = next;
        }

        public Task InvokeAsync( HttpContext c, ScopedHttpContext scoped )
        {
            Throw.DebugAssert( scoped.HttpContext is null );
            scoped.HttpContext = c;
            return _next( c );
        }
    }

}
