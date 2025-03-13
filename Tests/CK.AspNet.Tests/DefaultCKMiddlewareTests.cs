using CK.Core;
using CK.Monitoring;
using CK.Testing;
using Shouldly;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace CK.AspNet.Tests;

[TestFixture]
public class DefaultCKMiddlewareTests
{
    [Test]
    public async Task CKMiddleware_handles_exceptions_but_does_not_swallow_them_Async()
    {
        Throw.DebugAssert( GrandOutput.Default != null );

        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton<StupidService>();
        builder.Services.AddScoped<IActivityMonitor, ActivityMonitor>();
        builder.AppendApplicationBuilder( app => app.UseMiddleware<StupidMiddleware>() );

        int rootExceptionCount = 0;
        builder.PrependApplicationBuilder( app => app.Use( async ( context, next ) =>
        {
            try
            {
                await next.Invoke();
            }
            catch( Exception )
            {
                ++rootExceptionCount;
                throw;
            }
        } ), beforeCKMiddleware: true );

        int rootExceptionCountBefore = 0;
        builder.PrependApplicationBuilder( app => app.Use( async ( context, next ) =>
        {
            try
            {
                await next.Invoke();
            }
            catch( Exception )
            {
                ++rootExceptionCountBefore;
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            }
        } ), beforeCKMiddleware: true );

        await using var runningServer = await builder.CreateRunningAspNetServerAsync();

        using( var logs = GrandOutput.Default.CreateMemoryCollector( 500 ) )
        {
            using( HttpResponseMessage bug = await runningServer.Client.GetAsync( "?bug" ) )
            {
                bug.StatusCode.ShouldBe( HttpStatusCode.InternalServerError );
                await Task.Delay( 100 );
                logs.ExtractCurrentTexts().ShouldContain( "Bug!" );
            }
            using( HttpResponseMessage asyncBug = await runningServer.Client.GetAsync( "?asyncBug" ) )
            {
                asyncBug.StatusCode.ShouldBe( HttpStatusCode.InternalServerError );
                await Task.Delay( 100 );
                logs.ExtractCurrentTexts().ShouldContain( "AsyncBug!" );
            }
        }
        rootExceptionCountBefore.ShouldBe( 2 );
        rootExceptionCount.ShouldBe( 2 );
    }

    [Test]
    public async Task CKMiddleware_handles_ScopedHttpContext_Async()
    {
        bool testDone = false;
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddScoped<HttpContextDependentService>();
        builder.AppendApplicationBuilder( app => app.Use( ( context, next ) =>
        {
            var s = context.RequestServices.GetService<HttpContextDependentService>( true );
            s.HttpContextIsHere.ShouldBeTrue();
            testDone = true;
            return next();
        } ) );
        builder.AppendApplicationBuilder( app => app.Run( context =>
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unused;
            return Task.CompletedTask;
        } ) );

        await using var runningServer = await builder.CreateRunningAspNetServerAsync();

        using( HttpResponseMessage test = await runningServer.Client.GetAsync( "" ) )
        {
            test.StatusCode.ShouldBe( HttpStatusCode.Unused );
        }
        testDone.ShouldBeTrue();

        // when not in a request, obviously this fails.
        using( var scope = runningServer.Services.CreateScope() )
        {
            var dependent = scope.ServiceProvider.GetRequiredService<HttpContextDependentService>();
            dependent.HttpContextIsHere.ShouldBeFalse();
        }

    }

}
