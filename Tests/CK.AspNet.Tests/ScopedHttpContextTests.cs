using CK.Core;
using CK.Monitoring;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration.Memory;
using System.Linq;
using CK.Monitoring.Handlers;
using System.Threading;
using Microsoft.Extensions.Configuration.Json;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Reflection;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using CK.AspNet.Tester;

namespace CK.AspNet.Tests
{
    [TestFixture]
    public class ScopedHttpContextTests
    {
        [TestCase( "NoScopedHttpContext" )]
        [TestCase( "WithScopedHttpContext" )]
        public async Task there_is_no_scoped_HttpContext_injection_by_default( string mode )
        {
            bool testDone = false;
            var builder = new WebHostBuilder();
            builder.ConfigureServices( services =>
            {
                services.AddScoped<HttpContextDependentService>();
            } );
            builder.Configure( app =>
            {
                app.Use( ( context, next ) =>
                {
                    try
                    {
                        var s = context.RequestServices.GetService<HttpContextDependentService>( true );
                        s.HttpContextIsHere.Should().BeTrue();
                        mode.Should().Be( "WithScopedHttpContext" );
                        testDone = true;
                    }
                    catch( InvalidOperationException ex )
                    {
                        ex.Message.Should().Be( "Unable to resolve service for type 'CK.AspNet.ScopedHttpContext' while attempting to activate 'CK.AspNet.Tests.HttpContextDependentService'." );
                        mode.Should().Be( "NoScopedHttpContext" );
                        testDone = true;
                    }
                    return next();
                } );
                app.Run( context =>
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Unused;
                    return Task.CompletedTask;
                } );
            } );
            if( mode == "WithScopedHttpContext" )
            {
                builder.UseScopedHttpContext();
            }
            using( var client = new TestServerClient( new TestServer( builder ) ) )
            {
                using( HttpResponseMessage test = await client.Get( "" ) )
                {
                    test.StatusCode.Should().Be( HttpStatusCode.Unused );
                }
            }
            testDone.Should().BeTrue();
        }


    }
}
