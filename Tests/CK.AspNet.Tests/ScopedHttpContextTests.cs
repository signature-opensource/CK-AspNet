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
using Microsoft.Extensions.Hosting;

namespace CK.AspNet.Tests
{
    [TestFixture]
    public class ScopedHttpContextTests
    {
        [TestCase( "NoScopedHttpContext" )]
        [TestCase( "WithScopedHttpContext" )]
        public async Task there_is_no_scoped_HttpContext_injection_by_default_Async( string mode )
        {
            bool testDone = false;

            using( IHost host = await new HostBuilder().ConfigureWebHost( webBuilder =>
            {
                webBuilder
                .ConfigureServices( services => services.AddScoped<HttpContextDependentService>() )
                .Configure( conf =>
                    {
                        conf.Use( ( context, next ) =>
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
                        conf.Run( context =>
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.Unused;
                            return Task.CompletedTask;
                        } );
                    }
                ).UseTestServer();
                if( mode == "WithScopedHttpContext" )
                {
                    webBuilder.UseScopedHttpContext();
                }
                
            } ).StartAsync() )
            using( var client = new TestServerClient( host ) )
            using( HttpResponseMessage test = await client.Get( "" ) )
            {
                test.StatusCode.Should().Be( HttpStatusCode.Unused );
            }
            testDone.Should().BeTrue();
        }

#if DEBUG
        // This test relies on a Debug.Assert in the injected middleware:
        // it is useless in Release.
        [Test]
        public async Task duplicated_UseScopedHttpContext_are_ignored_Async()
        {
            using( var host = await new HostBuilder().ConfigureWebHost(
                builder =>
                {
                    builder.UseTestServer();
                    builder.UseScopedHttpContext();
                    builder.UseScopedHttpContext();
                    builder.Configure( app =>
                    {
                        app.Run( context =>
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.Unused;
                            return Task.CompletedTask;
                        } );
                    } );
                }
            ).StartAsync() )
            using( var client = new TestServerClient( host ) )
            {
                using( HttpResponseMessage test = await client.Get( "" ) )
                {
                    test.StatusCode.Should().Be( HttpStatusCode.Unused );
                }
            }
        }
#endif

    }
}
