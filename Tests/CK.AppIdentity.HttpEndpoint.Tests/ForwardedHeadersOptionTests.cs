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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.HttpEndpoint.Tests;

[TestFixture]
public class ForwardedHeadersOptionTests
{
    static IStObjMap _map = null!;
    static readonly HttpClient _client = new HttpClient();

    [OneTimeSetUp]
    public async Task BuildMapAsync()
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Path = TestHelper.BinFolder;
        configuration.FirstBinPath.Types.Add( typeof( ApplicationIdentityService ),
                                              typeof( HttpEndpointFeatureDriver ),
                                              typeof( HttpEndpointHostRegistration ),
                                              typeof( PartyComponentOptionDirectory ),
                                              typeof( ForwardedHeadersOption ) );
        _map = (await configuration.RunSuccessfullyAsync()).LoadMap();
    }

    static IHost BuildHost( Action<Dictionary<string, string?>>? configure = null )
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
            ["CK-AppIdentity:Local:AllowFeatures"] = "HttpEndpoint",
            ["CK-AppIdentity:Local:HttpEndpoint:Main:Urls"] = "http://127.0.0.1:0"
        };
        configure?.Invoke( values );
        b.Configuration.AddInMemoryCollection( values );
        b.AddApplicationIdentityServiceConfiguration();
        b.Services.AddScoped<IActivityMonitor, ActivityMonitor>();
        b.Services.AddScoped( sp => sp.GetRequiredService<IActivityMonitor>().ParallelLogger );
        b.AddHttpEndpoint( "Main", web => web.AppendApplicationBuilder( a =>
        {
            // What the pipeline sees of the client.
            ((WebApplication)a).MapGet( "/client", ( HttpContext c ) => $"{c.Request.Scheme}://{c.Request.Host} {c.Connection.RemoteIpAddress}" );
        } ) );
        b.Services.AddStObjMap( TestHelper.Monitor, _map );
        return b.CKBuild();
    }

    static async Task<string> GetClientAsync( IHost host, CancellationToken cancel )
    {
        var address = host.Services.GetRequiredService<HttpEndpointFeatureDriver>().Endpoints[0].Urls[0];
        using var request = new HttpRequestMessage( HttpMethod.Get, $"{address}/client" );
        request.Headers.Add( "X-Forwarded-Proto", "https" );
        request.Headers.Add( "X-Forwarded-Host", "public.example" );
        request.Headers.Add( "X-Forwarded-For", "203.0.113.7" );
        using var response = await _client.SendAsync( request, cancel );
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync( cancel );
    }

    [Test]
    [CancelAfter( 20000 )]
    public async Task the_headers_of_a_known_proxy_are_applied_Async( CancellationToken cancel )
    {
        using var host = BuildHost( c => c["CK-AppIdentity:Local:HttpEndpoint:Main:ForwardedHeaders:KnownProxies"] = "127.0.0.1" );
        await host.StartAsync( cancel );
        (await GetClientAsync( host, cancel )).ShouldBe( "https://public.example 203.0.113.7" );
        await host.StopAsync( cancel );
    }

    [Test]
    [CancelAfter( 20000 )]
    public async Task the_headers_of_a_known_network_are_applied_Async( CancellationToken cancel )
    {
        using var host = BuildHost( c => c["CK-AppIdentity:Local:HttpEndpoint:Main:ForwardedHeaders:KnownNetworks:0"] = "127.0.0.0/8" );
        await host.StartAsync( cancel );
        (await GetClientAsync( host, cancel )).ShouldBe( "https://public.example 203.0.113.7" );
        await host.StopAsync( cancel );
    }

    [TestCase( "NoOption" )]
    [TestCase( "OtherProxy" )]
    [CancelAfter( 20000 )]
    public async Task the_headers_are_ignored_without_the_option_or_from_an_unknown_proxy_Async( string mode, CancellationToken cancel )
    {
        using var host = BuildHost( c =>
        {
            if( mode == "OtherProxy" ) c["CK-AppIdentity:Local:HttpEndpoint:Main:ForwardedHeaders:KnownProxies"] = "10.0.0.12";
        } );
        await host.StartAsync( cancel );
        var client = await GetClientAsync( host, cancel );
        client.ShouldStartWith( "http://127.0.0.1:" );
        client.ShouldEndWith( " 127.0.0.1" );
        await host.StopAsync( cancel );
    }

    [TestCase( "true", "Main:ForwardedHeaders\" must be a section with \"KnownProxies\" or \"KnownNetworks\"" )]
    [TestCase( "NoProxy", "must have \"KnownProxies\" or \"KnownNetworks\": the proxies are never trusted by default." )]
    [TestCase( "InvalidProxy", "'not-an-ip' is not an IP address." )]
    [TestCase( "InvalidNetwork", "'10.0.0.0' is not a CIDR network" )]
    [TestCase( "InvalidHeaders", "Invalid \"CK-AppIdentity:Local:HttpEndpoint:Main:ForwardedHeaders:Headers\": 'XForwardedNothing'." )]
    [TestCase( "UnknownOptionKey", "Unknown \"CK-AppIdentity:Local:HttpEndpoint:Main:ForwardedHeaders:Trust\"" )]
    [TestCase( "UnknownEndpointKey", "Unknown \"CK-AppIdentity:Local:HttpEndpoint:Main:Compression\": the keys of an endpoint are Urls, Kestrel, DefaultEndpoint and its options (ForwardedHeaders)." )]
    [CancelAfter( 20000 )]
    public async Task configuration_errors_fail_the_start_Async( string error, string expected, CancellationToken cancel )
    {
        const string p = "CK-AppIdentity:Local:HttpEndpoint:Main:";
        using var host = BuildHost( c =>
        {
            switch( error )
            {
                case "true": c[p + "ForwardedHeaders"] = "true"; break;
                case "NoProxy": c[p + "ForwardedHeaders:ForwardLimit"] = "2"; break;
                case "InvalidProxy": c[p + "ForwardedHeaders:KnownProxies"] = "127.0.0.1;not-an-ip"; break;
                case "InvalidNetwork": c[p + "ForwardedHeaders:KnownNetworks"] = "10.0.0.0"; break;
                case "InvalidHeaders":
                    c[p + "ForwardedHeaders:KnownProxies"] = "127.0.0.1";
                    c[p + "ForwardedHeaders:Headers"] = "XForwardedNothing";
                    break;
                case "UnknownOptionKey":
                    c[p + "ForwardedHeaders:KnownProxies"] = "127.0.0.1";
                    c[p + "ForwardedHeaders:Trust"] = "all";
                    break;
                case "UnknownEndpointKey": c[p + "Compression"] = "true"; break;
            }
        } );
        using var logs = new AllMonitorsTexts();
        await Should.ThrowAsync<Exception>( () => host.StartAsync( cancel ) );
        (await logs.WaitForAsync( t => t.Contains( expected ) )).ShouldBeTrue( $"Expected: {expected}" );
    }
}
