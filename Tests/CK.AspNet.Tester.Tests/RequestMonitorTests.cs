using CK.Monitoring;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace CK.AspNet.Tester.Tests
{
    [TestFixture]
    public class RequestMonitorTests
    {
        [TestCase( true )]
        [TestCase( false )]
        public async Task request_monitor_handles_exceptions_but_does_not_swallow_them_by_default( bool swallow )
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
                        app.UseRequestMonitor( o => { o.SwallowErrors = swallow; } );
                        app.UseMiddleware<StupidMiddleware>();
                    } );
                var server = new TestServer( b );
                var client = new TestServerClient( server );

                using( HttpResponseMessage bug = await client.Get( "?bug" ) )
                {
                    Assert.That( bug.StatusCode, Is.EqualTo( HttpStatusCode.InternalServerError ) );
                    Assert.That( text.GetText().Contains( "/?bug" ) );
                    Assert.That( text.GetText().Contains( "Bug!" ) );

                }
                using( HttpResponseMessage asyncBug = await client.Get( "?asyncBug" ) )
                {
                    Assert.That( asyncBug.StatusCode, Is.EqualTo( HttpStatusCode.InternalServerError ) );
                    Assert.That( text.GetText().Contains( "/?asyncBug" ) );
                    Assert.That( text.GetText().Contains( "AsyncBug!" ) );
                }
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
        public async Task hidden_async_bugs_aka_Task_UnobservedExceptions_are_not_caught_at_all_by_the_RequestMonitor()
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
                        app.UseRequestMonitor();
                        app.UseMiddleware<StupidMiddleware>();
                    } );
                var server = new TestServer( b );
                var client = new TestServerClient( server );

                using( HttpResponseMessage hiddenBug = await client.Get( "?hiddenAsyncBug" ) )
                {
                    Assert.That( hiddenBug.StatusCode, Is.EqualTo( HttpStatusCode.Accepted ) );
                    Assert.That( text.GetText().Contains( "/?hiddenAsyncBug" ) );
                    Assert.That( text.GetText().Contains( "hiddenAsyncBug!" ), Is.False );
                }
            }
            finally
            {
                GrandOutput.Default.Dispose();
            }
        }

    }
}
