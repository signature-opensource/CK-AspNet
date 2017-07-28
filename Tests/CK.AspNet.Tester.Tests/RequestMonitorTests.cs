using CK.Monitoring;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Net;
using System.Net.Http;

namespace CK.AspNet.Tester.Tests
{
    [TestFixture]
    public class RequestMonitorTests
    {
        [TestCase( true )]
        [TestCase( false )]
        public void request_monitor_handles_exceptions_but_does_not_swallow_them_by_default( bool swallow )
        {
            var text = new TextGrandOutputHandlerConfiguration();
            var config = new GrandOutputConfiguration();
            config.AddHandler( text );
            GrandOutput.EnsureActiveDefault( config );
            int rootExceptionCount = 0;
            try
            {
                var b = WebHostBuilderFactory.Create( null, null,
                    services =>
                    {
                        services.AddSingleton<StupidService>();
                    },
                    app =>
                    {
                        app.Use( async ( context, next ) => 
                        {
                            try
                            {
                                await next.Invoke();
                            }
                            catch
                            {
                                ++rootExceptionCount;
                                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                            }
                        } );
                        app.UseRequestMonitor( new RequestMonitorMiddlewareOptions() { SwallowErrors = swallow } );
                        app.UseMiddleware<StupidMiddleware>();
                    } );
                var server = new TestServer( b );
                var client = new TestServerClient( server );

                HttpResponseMessage bug = client.Get( "?bug" );
                Assert.That( bug.StatusCode, Is.EqualTo( HttpStatusCode.InternalServerError ) );
                Assert.That( text.GetText().Contains( "/?bug" ) );
                Assert.That( text.GetText().Contains( "Bug!" ) );

                HttpResponseMessage asyncBug = client.Get( "?asyncBug" );
                Assert.That( bug.StatusCode, Is.EqualTo( HttpStatusCode.InternalServerError ) );
                Assert.That( text.GetText().Contains( "/?asyncBug" ) );
                Assert.That( text.GetText().Contains( "AsyncBug!" ) );
            }
            finally
            {
                GrandOutput.Default.Dispose();
            }
            if( swallow )
            {
                Assert.That( rootExceptionCount, Is.EqualTo( 0 ) );
            }
            else
            {
                Assert.That( rootExceptionCount, Is.EqualTo( 2 ) );
            }
        }

        [Test]
        public void hidden_async_bugs_are_not_caught_at_all()
        {
            var text = new TextGrandOutputHandlerConfiguration();
            var config = new GrandOutputConfiguration();
            config.AddHandler( text );
            GrandOutput.EnsureActiveDefault( config );
            try
            {
                var b = WebHostBuilderFactory.Create( null, null,
                    services =>
                    {
                        services.AddSingleton<StupidService>();
                    },
                    app =>
                    {
                        app.UseRequestMonitor( new RequestMonitorMiddlewareOptions() { SwallowErrors = true } );
                        app.UseMiddleware<StupidMiddleware>();
                    } );
                var server = new TestServer( b );
                var client = new TestServerClient( server );

                HttpResponseMessage hiddenBug = client.Get( "?hiddenAsyncBug" );
                Assert.That( text.GetText().Contains( "/?hiddenAsyncBug" ) );
                Assert.That( text.GetText().Contains( "hiddenAsyncBug!" ), Is.False );
                Assert.That( hiddenBug.StatusCode, Is.EqualTo( HttpStatusCode.NotFound ) );
            }
            finally
            {
                GrandOutput.Default.Dispose();
            }
        }
    }
}
