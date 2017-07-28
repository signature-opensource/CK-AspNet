using FluentAssertions;
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
            using( var client = new TestServerClient( CreateStupidServer() ) )
            {
                HttpResponseMessage notFound = client.Get( "other" );
                Assert.That( notFound.StatusCode, Is.EqualTo( HttpStatusCode.NotFound ) );

                HttpResponseMessage hello = client.Get( "?sayHello" );
                Assert.That( hello.StatusCode, Is.EqualTo( HttpStatusCode.OK ) );
                var content = hello.Content.ReadAsStringAsync().Result;
                Assert.That( content.StartsWith( "Hello! " ) );
            }
        }

        [Test]
        public void authorization_token_works()
        {
            using( var client = new TestServerClient( CreateStupidServer() ) )
            {
                client.Token = "my token";
                HttpResponseMessage m = client.Get( $"?readHeader&name={client.AuthorizationHeaderName}" );
                m.Content.ReadAsStringAsync().Result.Should().Be( $"header '{client.AuthorizationHeaderName}': 'Bearer my token'" );

            }
        }

        [Test]
        public void testing_PostXml()
        {
            using( var client = new TestServerClient( CreateStupidServer() ) )
            {
                HttpResponseMessage m = client.PostXml( "?rewriteXElement", "<a  >  <b/> </a>" );
                m.Content.ReadAsStringAsync().Result.Should().Be( "XElement: '<a><b /></a>'" );
            }
        }

        [Test]
        public void testing_PostJSON()
        {
            using( var client = new TestServerClient( CreateStupidServer() ) )
            {
                HttpResponseMessage m = client.PostJSON( "?rewriteJSON", @"{ ""a""  : null, ""b"" : {}  }" );
                m.Content.ReadAsStringAsync().Result.Should().Be( @"JSON: '{""a"":null,""b"":{}}'" );
            }
        }

        static TestServer CreateStupidServer()
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
            return server;
        }

    }
}
