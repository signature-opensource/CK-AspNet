using CK.Core;
using CK.Monitoring;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration.Memory;
using System.Linq;
using CK.Monitoring.Handlers;

namespace CK.AspNet.Tester.Tests
{
    [TestFixture]
    public class GrandOutputWebHostTests
    {
        [Test]
        public void grand_output_should_be_configured_with_default_values()
        {
            using( var client = new TestServerClient( CreateServerWithUseMonitoring() ) )
            {
                SystemActivityMonitor.RootLogPath.Should().NotBeNull().And.Contain( "Logs" );
                GrandOutput.Default.Should().NotBeNull();
            }
        }

        [Test]
        public void grand_output_can_be_configured_via_webhost_builder()
        {
            using( var client = new TestServerClient( CreateServerWithUseMonitoring( new GrandOutputOptions
            {
                RootLogPath = "Logs"
            } ) ) )
            {
                SystemActivityMonitor.RootLogPath.Should().NotBeNull().And.Contain( "Logs" );
                GrandOutput.Default.Should().NotBeNull();
            }
        }

        [Test]
        public void grand_output_configuration_from_configurationmodel()
        {
            var createHandler = GrandOutput.CreateHandler;
            try
            {
                var configSource = new MemoryConfigurationSource
                {
                    InitialData = new Dictionary<string, string> { { "GrandOutput:TimerDuration", TimeSpan.FromSeconds( 10 ).ToString() } }
                };

                using( var client = new TestServerClient( CreateServerWithUseMonitoring( "GrandOutput", configSource, out IConfigurationRoot configRoot ) ) )
                {
                    SystemActivityMonitor.RootLogPath.Should().NotBeNull().And.Contain( "Logs" );
                    GrandOutput.Default.Should().NotBeNull();

                    var configProvider = configRoot.Providers.OfType<MemoryConfigurationProvider>().First();
                    configProvider.Set( "GrandOutput:TimerDuration", TimeSpan.FromSeconds( 2 ).ToString() );
                    configProvider.Add( "GrandOutput:TextFile:Path", "Monitoring" );
                    configProvider.Add( "GrandOutput:TextFile:MaxCountPerFile", "10" );

                    int newHandlerCreated = 0;
                    GrandOutput.CreateHandler = ( handlerConfig ) =>
                    {
                        newHandlerCreated++;
                        handlerConfig.Should().BeOfType<TextFileConfiguration>();
                        TextFileConfiguration textFileConfiguration = (TextFileConfiguration)handlerConfig;
                        textFileConfiguration.MaxCountPerFile.Should().Be( 10 );
                        return createHandler( handlerConfig );
                    };

                    var section = configRoot.GetSection( "GrandOutput" );
                    var reloadToken = section.GetReloadToken();
                    configRoot.Reload();

                    reloadToken.HasChanged.Should().BeTrue();
                    newHandlerCreated.Should().Be( 1 );

                    GrandOutput.CreateHandler = ( handlerConfig ) =>
                    {
                        newHandlerCreated++;
                        handlerConfig.Should().BeOfType<BinaryFileConfiguration>();
                        BinaryFileConfiguration configuration = (BinaryFileConfiguration)handlerConfig;
                        configuration.MaxCountPerFile.Should().Be( 100 );
                        configuration.UseGzipCompression.Should().BeTrue();

                        return createHandler( handlerConfig );
                    };

                    configProvider.Set( "GrandOutput:BinaryFile:MaxCountPerFile", "100" );
                    configProvider.Set( "GrandOutput:BinaryFile:UseGzipCompression", "True" );
                    configRoot.Reload();

                    newHandlerCreated.Should().Be( 2 );
                }
            }
            finally
            {
                GrandOutput.CreateHandler = createHandler;
            }
        }

        [Test]
        public void grand_output_configuration_create_handler_exception()
        {
            var createHandler = GrandOutput.CreateHandler;
            try
            {
                GrandOutput.CreateHandler = ( handlerConfig ) =>
                {
                    throw new Exception( "Ouin" );
                    return createHandler( handlerConfig );
                };
                using( var client = new TestServerClient( CreateServerWithUseMonitoring( new GrandOutputOptions
                {
                    TextFile = new TextFileConfiguration
                    {
                        Path = "Monitoring"
                    }
                } ) ) )
                {
                    var config = new GrandOutputConfiguration();
                    config.AddHandler( new TextFileConfiguration { Path = "Monitoring" } );
                    GrandOutput.EnsureActiveDefault( config );
                }
            }
            finally
            {
                GrandOutput.CreateHandler = createHandler;
            }
        }

        static TestServer CreateServerWithUseMonitoring() => CreateServerWithUseMonitoring<GrandOutputOptions>();
        static TestServer CreateServerWithUseMonitoring<TConfig>( TConfig options = null ) where TConfig : GrandOutputOptions
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

            b.UseMonitoring( options );
            var server = new TestServer( b );
            return server;
        }
        static TestServer CreateServerWithUseMonitoring( string configSection, IConfigurationSource config, out IConfigurationRoot configRoot )
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

            //configRoot = new ConfigurationBuilder();
            b.ConfigureAppConfiguration( ( ctx, configBuilder ) =>
            {
                configBuilder.Add( config );
            } );
            b.UseMonitoring<GrandOutputOptions>( configSection );
            var server = new TestServer( b );
            configRoot = server.Host.Services.GetRequiredService<IConfiguration>() as IConfigurationRoot;
            return server;
        }
    }
}
