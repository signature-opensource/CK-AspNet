using System;
using NUnit.Framework;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.Net.Http;
using System.Net;

namespace CK.AspNet.Tester.Tests
{
    [TestFixture]
    public class WebHostFactoryTests
    {

        public class StupidService
        {
            public string GetText() => $"It is {DateTime.UtcNow}.";
        }

        public class StupidMiddleware
        {
            readonly RequestDelegate _next;
            readonly StupidService _s;

            public StupidMiddleware(RequestDelegate next, StupidService s )
            {
                _next = next;
                _s = s;
            }

            public Task Invoke(HttpContext context)
            {
                if( context.Request.Query.ContainsKey("sayHello") )
                {
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    return context.Response.WriteAsync("Hello! " + _s.GetText());
                }
                return _next.Invoke(context);
            }
        }

        [Test]
        public void hello_world_midleware()
        {
            var b = WebHostBuilderFactory.Create(null, null,
                services =>
                {
                    services.AddSingleton<StupidService>();
                },
                app =>
                {
                    app.UseMiddleware<StupidMiddleware>();
                });
            var server = new TestServer(b);
            var client = new TestServerClient(server);

            HttpResponseMessage notFound = client.Get("other");
            Assert.That(notFound.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

            HttpResponseMessage hello = client.Get("?sayHello");
            Assert.That(hello.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var content = hello.Content.ReadAsStringAsync().Result;
            Assert.That(content.StartsWith("Hello! ") );
        }
    }
}
