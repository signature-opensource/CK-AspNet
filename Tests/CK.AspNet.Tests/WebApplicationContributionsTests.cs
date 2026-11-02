using CK.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CK.AspNet.Tests;

[TestFixture]
public class WebApplicationContributionsTests
{
    static int _enableCount;

    // A feature enabled by the "Enable" convention: its middleware is registered once whatever the number of calls.
    static WebApplicationBuilder EnableHello( WebApplicationBuilder builder )
    {
        if( builder.TryEnable( "Hello" ) )
        {
            ++_enableCount;
            builder.AppendApplicationBuilder( app => app.Use( async ( HttpContext context, RequestDelegate next ) =>
            {
                context.Response.Headers.Append( "X-Hello", "hello" );
                await next( context );
            } ) );
        }
        return builder;
    }

    [Test]
    public void FindOrAdd_registers_one_instance_per_service_collection()
    {
        var services = new ServiceCollection();
        var c = WebApplicationContributions.FindOrAdd( services );
        WebApplicationContributions.FindOrAdd( services ).ShouldBeSameAs( c );
        services.Count( d => d.ServiceType == typeof( WebApplicationContributions ) ).ShouldBe( 1 );
        services.BuildServiceProvider().GetRequiredService<WebApplicationContributions>().ShouldBeSameAs( c );

        WebApplicationContributions.FindOrAdd( new ServiceCollection() ).ShouldNotBeSameAs( c );
    }

    [Test]
    public void TryEnable_is_true_once_per_builder_and_name()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.TryEnable( "A" ).ShouldBeTrue();
        builder.TryEnable( "A" ).ShouldBeFalse();
        builder.TryEnable( "B" ).ShouldBeTrue();
        WebApplication.CreateSlimBuilder().TryEnable( "A" ).ShouldBeTrue();
    }

    [Test]
    public async Task the_contributions_apply_in_their_order_and_an_Enable_extension_is_idempotent_Async()
    {
        _enableCount = 0;
        var order = new List<int>();
        var contributions = WebApplicationContributions.FindOrAdd( new ServiceCollection() );
        contributions.Add( b => { order.Add( 1 ); EnableHello( b ); } );
        contributions.Add( b => { order.Add( 2 ); EnableHello( b ); } );

        var builder = WebApplication.CreateSlimBuilder();
        contributions.ApplyTo( builder );
        // The application enables it too.
        EnableHello( builder );
        order.ShouldBe( [1, 2] );
        _enableCount.ShouldBe( 1 );

        await using var runningServer = await builder.CreateRunningAspNetServerAsync();
        using var response = await runningServer.Client.GetAsync( "" );
        response.Headers.GetValues( "X-Hello" ).ShouldBe( ["hello"] );
    }
}
