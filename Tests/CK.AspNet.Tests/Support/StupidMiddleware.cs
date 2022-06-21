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
using System.Threading;
using CK.Core;

namespace CK.AspNet.Tests
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

        public async Task InvokeAsync( HttpContext context, IActivityMonitor monitor )
        {
            monitor.Warn( "StupidMiddleware is here!" );
            if( context.Request.Query.ContainsKey( "sayHello" ) )
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                await context.Response.WriteAsync( "Hello! " + _s.GetText() );
                return;
            }
            if( context.Request.Query.ContainsKey( "readHeader" ) )
            {
                string name = context.Request.Query["name"];
                StringValues header = context.Request.Headers[name];
                await context.Response.WriteAsync( $"header '{name}': '{header}'" );
                return;
            }
            if( context.Request.Query.ContainsKey( "rewriteJSON" ) )
            {
                if( !HttpMethods.IsPost( context.Request.Method ) ) context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                string content = await new StreamReader( context.Request.Body ).ReadToEndAsync();
                await context.Response.WriteAsync( $"JSON: '{JObject.Parse( content ).ToString( Newtonsoft.Json.Formatting.None )}'" );
                return;
            }
            if( context.Request.Query.ContainsKey( "rewriteXElement" ) )
            {
                if( !HttpMethods.IsPost( context.Request.Method ) ) context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                string content = await new StreamReader( context.Request.Body ).ReadToEndAsync();
                await context.Response.WriteAsync( $"XElement: '{XElement.Parse( content ).ToString( SaveOptions.DisableFormatting )}'" );
                return;
            }
            if( context.Request.Query.ContainsKey( "bug" ) )
            {
                throw new Exception( "Bug!" );
            }
            if( context.Request.Query.ContainsKey( "asyncBug" ) )
            {
                await BugAsync();
                return;
            }
            if( context.Request.Query.ContainsKey( "hiddenAsyncBug" ) )
            {
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                _ = Task.Delay( 100 ).ContinueWith( t =>
                {
                    throw new Exception( "I'm an horrible HiddenAsyncBug!" );
                }, TaskScheduler.Default );
                await context.Response.WriteAsync( "Will break the started Task." );
                return;
            }
            if( context.Request.Query.ContainsKey( "unhandledAppDomainException" ) )
            {
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                var t = new Thread( () => throw new Exception( "I'm an unhandled exception." ) );
                t.IsBackground = true;
                t.Start();
                await context.Response.WriteAsync( "Will break the started thread." );
                return;
            }
            await _next.Invoke( context );
        }

        async Task BugAsync()
        {
            await Task.Delay( 100 );
            throw new Exception( "AsyncBug!" );
        }
    }

}
