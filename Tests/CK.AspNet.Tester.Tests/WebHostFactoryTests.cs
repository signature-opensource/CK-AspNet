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
    public class WebHostFactoryTests
    {
        [Test]
        public void hello_world_midleware()
        {
            var b = WebHostBuilderFactory.Create( null, null,
                services =>
                {
                    services.AddSingleton<StupidService>();
                },
                app =>
                {
                    app.UseMiddleware<StupidMiddleware>();
                } );
            var server = new TestServer( b );
            var client = new TestServerClient( server );

            HttpResponseMessage notFound = client.Get( "other" );
            Assert.That( notFound.StatusCode, Is.EqualTo( HttpStatusCode.NotFound ) );

            HttpResponseMessage hello = client.Get( "?sayHello" );
            Assert.That( hello.StatusCode, Is.EqualTo( HttpStatusCode.OK ) );
            var content = hello.Content.ReadAsStringAsync().Result;
            Assert.That( content.StartsWith( "Hello! " ) );
        }
    }
}
