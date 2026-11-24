using CK.Core;
using CK.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.HttpEndpoint.Tests;

[TestFixture]
public class InProcessHttpEndpointOptionTests
{
    static IStObjMap _map = null!;

    [OneTimeSetUp]
    public async Task BuildMapAsync()
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Path = TestHelper.BinFolder;
        configuration.FirstBinPath.Types.Add( typeof( ApplicationIdentityService ),
                                              typeof( HttpEndpointFeatureDriver ),
                                              typeof( HttpEndpointHostRegistration ),
                                              typeof( PartyComponentOptionDirectory ),
                                              typeof( PartyComponentDirectory ),
                                              typeof( InProcessHttpEndpointOption ) );
        _map = (await configuration.RunSuccessfullyAsync()).LoadMap();
    }

    static IHost BuildHost( Action<Dictionary<string, string?>> configure )
    {
        var b = Host.CreateApplicationBuilder( new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            EnvironmentName = "Production",
            ContentRootPath = TestHelper.TestProjectFolder
        } );
        var values = new Dictionary<string, string?>
        {
            ["CK-AppIdentity:FullName"] = "Test/$Server/#Dev",
            ["CK-AppIdentity:Local:HttpEndpoint"] = "true"
        };
        configure( values );
        b.Configuration.AddInMemoryCollection( values );
        b.AddApplicationIdentityServiceConfiguration();
        b.Services.AddScoped<IActivityMonitor, ActivityMonitor>();
        b.Services.AddScoped( sp => sp.GetRequiredService<IActivityMonitor>().ParallelLogger );
        b.ConfigureHttpEndpoint( web => web.AppendApplicationBuilder( a =>
        {
            ((WebApplication)a).MapPost( "/echo", async ( HttpContext c ) =>
            {
                using var r = new StreamReader( c.Request.Body );
                return $"{c.Request.Scheme}://{c.Request.Host}{c.Request.Path} {await r.ReadToEndAsync()}";
            } );
        } ) );
        b.Services.AddStObjMap( TestHelper.Monitor, _map );
        return b.CKBuild();
    }

    static async Task<string> EchoAsync( RunningHttpEndpoint endpoint, string body, CancellationToken cancel )
    {
        var server = endpoint.GetInProcessServer();
        server.ShouldNotBeNull();
        var context = await server.SendAsync( c =>
        {
            c.Request.Method = "POST";
            c.Request.Path = "/echo";
            c.Request.Body = new MemoryStream( System.Text.Encoding.UTF8.GetBytes( body ) );
        }, cancel );
        context.Response.StatusCode.ShouldBe( 200 );
        using var r = new StreamReader( context.Response.Body );
        return await r.ReadToEndAsync( cancel );
    }

    [TestCase( "true", "app://localhost" )]
    [TestCase( "https://desktop.local", "https://desktop.local" )]
    [CancelAfter( 20000 )]
    public async Task the_endpoint_runs_in_process_Async( string config, string expectedUrl, CancellationToken cancel )
    {
        using var host = BuildHost( c =>
        {
            if( config == "true" ) c["CK-AppIdentity:Local:HttpEndpoint:InProcess"] = "true";
            else c["CK-AppIdentity:Local:HttpEndpoint:InProcess:Url"] = config;
        } );
        await host.StartAsync( cancel );
        var endpoint = host.Services.GetRequiredService<HttpEndpointFeatureDriver>().Endpoint;
        endpoint.ShouldNotBeNull();
        endpoint.DefaultUrl.ShouldBe( expectedUrl );
        endpoint.Urls.ShouldBe( [expectedUrl] );
        (await EchoAsync( endpoint, "Hello!", cancel )).ShouldBe( $"{expectedUrl}/echo Hello!" );
        await host.StopAsync( cancel );
    }

    [TestCase( "WithUrls", "InProcessHttpEndpointOption' provides the server of the endpoint (app://localhost): \"Urls\", \"Kestrel\" and \"DefaultEndpoint\" must be removed." )]
    [TestCase( "InvalidUrl", "Invalid \"Url\": 'app://localhost/path' must be an absolute URL without path" )]
    [TestCase( "UnknownKey", "Unknown \"Port\": the only key is Url." )]
    [CancelAfter( 20000 )]
    public async Task configuration_errors_fail_the_start_Async( string error, string expected, CancellationToken cancel )
    {
        const string p = "CK-AppIdentity:Local:HttpEndpoint:";
        using var host = BuildHost( c =>
        {
            switch( error )
            {
                case "WithUrls":
                    c[p + "InProcess"] = "true";
                    c[p + "Urls"] = "http://127.0.0.1:0";
                    break;
                case "InvalidUrl": c[p + "InProcess:Url"] = "app://localhost/path"; break;
                case "UnknownKey": c[p + "InProcess:Port"] = "12"; break;
            }
        } );
        using var logs = new AllMonitorsTexts();
        await Should.ThrowAsync<Exception>( () => host.StartAsync( cancel ) );
        (await logs.WaitForAsync( t => t.Contains( expected ) )).ShouldBeTrue( $"Expected: {expected}" );
    }
}
