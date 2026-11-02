using CK.AspNet;
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
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppIdentity.HttpEndpoint.Tests;

[TestFixture]
public class HttpEndpointTests
{
    // A host singleton: forwarded to the web applications (one instance).
    public sealed class SharedService : ISingletonAutoService, IDisposable
    {
        internal static int _disposeCount;
        public Guid Id { get; } = Guid.NewGuid();
        public void Dispose() => Interlocked.Increment( ref _disposeCount );
    }

    // A container-configured singleton: each web application has its own instance.
    [SingletonContainerConfiguredService]
    public class PerContainerService : ISingletonAutoService
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    // A package's contribution: registered by its real object, applied to every endpoint.
    public sealed class HelloPackage : IRealObject
    {
        void ConfigureServices( StObjContextRoot.ServiceRegister services )
        {
            WebApplicationContributions.FindOrAdd( services.Services ).Add( EnableContributedHello );
        }

        static void EnableContributedHello( WebApplicationBuilder web )
        {
            if( web.TryEnable( "ContributedHello" ) )
            {
                web.AppendApplicationBuilder( a => ((WebApplication)a).MapGet( "/contributed", () => "contributed" ) );
            }
        }
    }

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
                                              typeof( SharedService ),
                                              typeof( PerContainerService ),
                                              typeof( HelloPackage ) );
        _map = (await configuration.RunSuccessfullyAsync()).LoadMap();
    }

    static IHost BuildHost( Action<Dictionary<string, string?>>? configure = null,
                            Action<HostApplicationBuilder>? builder = null,
                            string environmentName = "Production" )
    {
        var b = Host.CreateApplicationBuilder( new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            EnvironmentName = environmentName,
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
        builder?.Invoke( b );
        b.Services.AddStObjMap( TestHelper.Monitor, _map );
        return b.CKBuild();
    }

    // Maps the routes of the tests on an endpoint (one gesture: CKBuild applies it).
    static void MapTestRoutes( WebApplicationBuilder web )
    {
        web.AppendApplicationBuilder( a =>
        {
            var app = (WebApplication)a;
            app.MapGet( "/hello", () => "hello" );
            app.MapGet( "/ids", ( SharedService s, PerContainerService p ) => $"{s.Id} {p.Id}" );
            app.MapGet( "/monitor", ( IActivityMonitor m, ScopedHttpContext c ) => ReferenceEquals( m, c.Monitor ) ? "same" : "different" );
        } );
    }

    static HttpEndpointFeatureDriver GetDriver( IHost host ) => host.Services.GetRequiredService<HttpEndpointFeatureDriver>();

    static string Address( IHost host, int index = 0 ) => GetDriver( host ).Endpoints[index].Urls[0];

    [Test]
    [CancelAfter( 20000 )]
    public async Task an_endpoint_answers_and_stops_with_the_host_Async( CancellationToken cancel )
    {
        using var host = BuildHost( builder: b => b.AddHttpEndpoint( "Main", MapTestRoutes ) );
        await host.StartAsync( cancel );
        var driver = GetDriver( host );
        var endpoint = driver.Endpoints.ShouldHaveSingleItem();
        endpoint.Name.ShouldBe( "Main" );
        var address = Address( host );
        address.ShouldNotEndWith( ":0" );
        (await _client.GetStringAsync( $"{address}/hello", cancel )).ShouldBe( "hello" );
        (await _client.GetStringAsync( $"{address}/monitor", cancel )).ShouldBe( "same" );

        await host.StopAsync( cancel );
        driver.Endpoints.ShouldBeEmpty();
        await Should.ThrowAsync<HttpRequestException>( () => _client.GetStringAsync( $"{address}/hello", cancel ) );
    }

    [Test]
    [CancelAfter( 20000 )]
    public async Task two_endpoints_share_the_host_singletons_and_have_their_own_container_configured_ones_Async( CancellationToken cancel )
    {
        using var host = BuildHost( c => c["CK-AppIdentity:Local:HttpEndpoint:Second:Urls"] = "http://127.0.0.1:0",
                                    b => b.AddHttpEndpoint( "Main", MapTestRoutes ).AddHttpEndpoint( "Second", MapTestRoutes ) );
        await host.StartAsync( cancel );
        GetDriver( host ).Endpoints.Count.ShouldBe( 2 );
        var main = (await _client.GetStringAsync( $"{Address( host, 0 )}/ids", cancel )).Split( ' ' );
        var second = (await _client.GetStringAsync( $"{Address( host, 1 )}/ids", cancel )).Split( ' ' );
        main[0].ShouldBe( second[0], "The host singleton is shared." );
        main[0].ShouldBe( host.Services.GetRequiredService<SharedService>().Id.ToString() );
        main[1].ShouldNotBe( second[1], "Each web application has its own container-configured singleton." );
        await host.StopAsync( cancel );
    }

    [TestCase( "http://127.0.0.1:0;http://[::1]:0", "http://127.0.0.1:" )]
    [TestCase( "http://[::1]:0;http://127.0.0.1:0", "http://[::1]:" )]
    [TestCase( "http://*:0", "http://localhost:" )]
    [CancelAfter( 20000 )]
    public async Task the_default_url_is_the_first_configured_one_and_is_browsable_Async( string urls, string expectedStart, CancellationToken cancel )
    {
        using var host = BuildHost( c => c["CK-AppIdentity:Local:HttpEndpoint:Main:Urls"] = urls, b => b.AddHttpEndpoint( "Main", MapTestRoutes ) );
        await host.StartAsync( cancel );
        var endpoint = GetDriver( host ).Endpoints.ShouldHaveSingleItem();
        var primary = endpoint.DefaultUrl.ShouldNotBeNull();
        primary.ShouldStartWith( expectedStart );
        primary.ShouldNotEndWith( ":0" );
        (await _client.GetStringAsync( $"{primary}/hello", cancel )).ShouldBe( "hello" );
        await host.StopAsync( cancel );
    }

    // The Kestrel endpoints are sorted by name ("A" then "B"): "DefaultEndpoint" names the one to use.
    [TestCase( null, "http://127.0.0.1:" )]
    [TestCase( "B", "http://[::1]:" )]
    [TestCase( "A", "http://127.0.0.1:" )]
    [CancelAfter( 20000 )]
    public async Task with_a_Kestrel_section_DefaultEndpoint_names_the_default_url_Async( string? defaultEndpoint, string expectedStart, CancellationToken cancel )
    {
        using var host = BuildHost( c =>
        {
            c.Remove( "CK-AppIdentity:Local:HttpEndpoint:Main:Urls" );
            c["CK-AppIdentity:Local:HttpEndpoint:Main:Kestrel:Endpoints:A:Url"] = "http://127.0.0.1:0";
            c["CK-AppIdentity:Local:HttpEndpoint:Main:Kestrel:Endpoints:B:Url"] = "http://[::1]:0";
            if( defaultEndpoint != null ) c["CK-AppIdentity:Local:HttpEndpoint:Main:DefaultEndpoint"] = defaultEndpoint;
        }, b => b.AddHttpEndpoint( "Main", MapTestRoutes ) );
        await host.StartAsync( cancel );
        var endpoint = GetDriver( host ).Endpoints.ShouldHaveSingleItem();
        endpoint.Urls.Count.ShouldBe( 2 );
        var primary = endpoint.DefaultUrl.ShouldNotBeNull();
        primary.ShouldStartWith( expectedStart );
        (await _client.GetStringAsync( $"{primary}/hello", cancel )).ShouldBe( "hello" );
        await host.StopAsync( cancel );
    }

    [Test]
    [CancelAfter( 20000 )]
    public async Task the_contributions_of_the_packages_apply_to_every_endpoint_Async( CancellationToken cancel )
    {
        // No application configuration at all for the endpoints: the packages do the job.
        using var host = BuildHost( c => c["CK-AppIdentity:Local:HttpEndpoint:Second:Urls"] = "http://127.0.0.1:0" );
        await host.StartAsync( cancel );
        GetDriver( host ).Endpoints.Count.ShouldBe( 2 );
        (await _client.GetStringAsync( $"{Address( host, 0 )}/contributed", cancel )).ShouldBe( "contributed" );
        (await _client.GetStringAsync( $"{Address( host, 1 )}/contributed", cancel )).ShouldBe( "contributed" );
        await host.StopAsync( cancel );
    }

    [Test]
    [CancelAfter( 20000 )]
    public async Task a_disposable_host_singleton_is_disposed_once_by_the_host_Async( CancellationToken cancel )
    {
        SharedService._disposeCount = 0;
        using( var host = BuildHost( builder: b => b.AddHttpEndpoint( "Main", MapTestRoutes ) ) )
        {
            await host.StartAsync( cancel );
            await _client.GetStringAsync( $"{Address( host )}/ids", cancel );
            await host.StopAsync( cancel );
            SharedService._disposeCount.ShouldBe( 0 );
        }
        SharedService._disposeCount.ShouldBe( 1 );
    }

    [Test]
    [CancelAfter( 20000 )]
    public async Task a_port_in_use_fails_the_start_of_the_host_Async( CancellationToken cancel )
    {
        using var listener = new TcpListener( IPAddress.Loopback, 0 );
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var host = BuildHost( c => c["CK-AppIdentity:Local:HttpEndpoint:Main:Urls"] = $"http://127.0.0.1:{port}" );
        await Should.ThrowAsync<Exception>( () => host.StartAsync( cancel ) );
        GetDriver( host ).Endpoints.ShouldBeEmpty();
    }

    [TestCase( "NoEndpoint" )]
    [TestCase( "UrlsAndKestrel" )]
    [TestCase( "UnknownApplicationEndpoint" )]
    [TestCase( "RootUrlsInDevelopment" )]
    [TestCase( "ConfiguredTenantDomain" )]
    [TestCase( "DefaultEndpointWithUrls" )]
    [TestCase( "UnknownDefaultEndpoint" )]
    [CancelAfter( 20000 )]
    public async Task configuration_errors_fail_the_initialization_Async( string error, CancellationToken cancel )
    {
        using var host = BuildHost( c =>
        {
            switch( error )
            {
                case "NoEndpoint": c.Remove( "CK-AppIdentity:Local:HttpEndpoint:Main:Urls" ); break;
                case "UrlsAndKestrel": c["CK-AppIdentity:Local:HttpEndpoint:Main:Kestrel:Limits:MaxRequestBodySize"] = "1000"; break;
                case "RootUrlsInDevelopment": c["Urls"] = "http://127.0.0.1:0"; break;
                case "DefaultEndpointWithUrls": c["CK-AppIdentity:Local:HttpEndpoint:Main:DefaultEndpoint"] = "Http"; break;
                case "UnknownDefaultEndpoint":
                    c.Remove( "CK-AppIdentity:Local:HttpEndpoint:Main:Urls" );
                    c["CK-AppIdentity:Local:HttpEndpoint:Main:Kestrel:Endpoints:Http:Url"] = "http://127.0.0.1:0";
                    c["CK-AppIdentity:Local:HttpEndpoint:Main:DefaultEndpoint"] = "Https";
                    break;
                case "ConfiguredTenantDomain":
                    c["CK-AppIdentity:Parties:0:FullName"] = "Tenant/$Tenant";
                    c["CK-AppIdentity:Parties:0:Local:AllowFeatures"] = "HttpEndpoint";
                    break;
            }
        },
        b => b.AddHttpEndpoint( error == "UnknownApplicationEndpoint" ? "Other" : "Main" ),
        environmentName: error == "RootUrlsInDevelopment" ? "Development" : "Production" );
        await Should.ThrowAsync<Exception>( () => host.StartAsync( cancel ) );
        GetDriver( host ).Endpoints.ShouldBeEmpty();
    }

    [Test]
    [CancelAfter( 20000 )]
    public async Task root_urls_are_only_a_warning_in_Production_Async( CancellationToken cancel )
    {
        using var host = BuildHost( c => c["Urls"] = "http://127.0.0.1:0", b => b.AddHttpEndpoint( "Main", MapTestRoutes ) );
        await host.StartAsync( cancel );
        (await _client.GetStringAsync( $"{Address( host )}/hello", cancel )).ShouldBe( "hello" );
        await host.StopAsync( cancel );
    }

    [Test]
    [CancelAfter( 20000 )]
    public async Task a_web_application_that_stops_stops_the_host_Async( CancellationToken cancel )
    {
        using var host = BuildHost( builder: b => b.AddHttpEndpoint( "Main" ) );
        await host.StartAsync( cancel );
        var hostLifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        hostLifetime.ApplicationStopping.IsCancellationRequested.ShouldBeFalse();
        GetDriver( host ).Endpoints[0].Application.Lifetime.StopApplication();
        hostLifetime.ApplicationStopping.IsCancellationRequested.ShouldBeTrue();
        await host.StopAsync( cancel );
    }

    [Test]
    [CancelAfter( 20000 )]
    public async Task a_dynamic_tenant_domain_cannot_have_endpoints_Async( CancellationToken cancel )
    {
        using var host = BuildHost( builder: b => b.AddHttpEndpoint( "Main" ) );
        await host.StartAsync( cancel );
        var s = host.Services.GetRequiredService<ApplicationIdentityService>();
        (await s.AddTenantDomainAsync( TestHelper.Monitor, c =>
        {
            c["FullName"] = "Tenant/$Tenant";
            c["Local:AllowFeatures"] = "HttpEndpoint";
        } )).ShouldBeNull();
        (await s.AddTenantDomainAsync( TestHelper.Monitor, c => c["FullName"] = "Tenant/$Tenant" )).ShouldNotBeNull();
        await host.StopAsync( cancel );
    }

    [Test]
    [CancelAfter( 20000 )]
    public async Task without_the_feature_the_application_configurations_are_ignored_Async( CancellationToken cancel )
    {
        using var host = BuildHost( c => c["CK-AppIdentity:Local:AllowFeatures"] = "", b => b.AddHttpEndpoint( "Main" ) );
        await host.StartAsync( cancel );
        GetDriver( host ).Endpoints.ShouldBeEmpty();
        await host.StopAsync( cancel );
    }
}
