using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Xml.Linq;

namespace CK.AspNet.Tester.Tests
{
    public class StupidMiddleware
    {
        readonly RequestDelegate _next;
        readonly StupidService _s;

        public StupidMiddleware( RequestDelegate next, StupidService s )
        {
            _next = next;
            _s = s;
        }

        public Task Invoke( HttpContext context )
        {
            if( context.Request.Query.ContainsKey( "sayHello" ) )
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                return context.Response.WriteAsync( "Hello! " + _s.GetText() );
            }
            if( context.Request.Query.ContainsKey( "readHeader" ) )
            {
                string name = context.Request.Query["name"];
                StringValues header = context.Request.Headers[name];
                return context.Response.WriteAsync( $"header '{name}': '{header}'" );
            }
            if( context.Request.Query.ContainsKey( "rewriteJSON" ) )
            {
                if( !HttpMethods.IsPost( context.Request.Method ) ) context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                string content = new StreamReader( context.Request.Body ).ReadToEnd();
                return context.Response.WriteAsync( $"JSON: '{JObject.Parse( content ).ToString( Newtonsoft.Json.Formatting.None )}'" );
            }
            if( context.Request.Query.ContainsKey( "rewriteXElement" ) )
            {
                if( !HttpMethods.IsPost( context.Request.Method ) ) context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                string content = new StreamReader( context.Request.Body ).ReadToEnd();
                return context.Response.WriteAsync( $"XElement: '{XElement.Parse( content ).ToString( SaveOptions.DisableFormatting )}'" );
            }
            if( context.Request.Query.ContainsKey( "bug" ) )
            {
                throw new Exception( "Bug!" );
            }
            if( context.Request.Query.ContainsKey( "asyncBug" ) )
            {
                return AsyncBug();
            }
            if( context.Request.Query.ContainsKey( "hiddenAsyncBug" ) )
            {
                Task.Delay( 100 ).ContinueWith( t =>
                {
                    throw new Exception( "HiddenAsyncBug!" );
                } );
            }
            return _next.Invoke( context );
        }

        async Task AsyncBug()
        {
            await Task.Delay( 100 );
            throw new Exception( "AsyncBug!" );
        }
    }

}
