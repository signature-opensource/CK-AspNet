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
using System.Threading;
using Microsoft.Extensions.Configuration.Json;
using System.Net.Http;
using System.IO;

namespace CK.AspNet.Tester.Tests
{
    [TestFixture]
    public class GrandOutputWebHostTests
    {
        [SetUp]
        public void GrandOutput_Default_should_be_configured_with_default_values()
        {
            using( var client = new TestServerClient( CreateServerWithUseMonitoring( null ) ) )
            {
                LogFile.RootLogPath.Should().NotBeNull().And.Contain( "Logs" );
                GrandOutput.Default.Should().NotBeNull();
            }
        }

        [Test]
        public async Task GrandOutput_configuration_from_Json()
        {
            var config = new DynamicJsonConfigurationSource(
                    @"{ ""Monitor"": {
                            ""GrandOutput"": {
                                ""Handlers"": {
                                    ""TextFile"": {
                                        ""Path"": ""GrandOutput_configuration_from_Json""
                                    }
                                }
                            }
                         }
                      }" );
            var g = new GrandOutput( new GrandOutputConfiguration() );
            var server = CreateServerWithUseMonitoring( config, g );
            var client = new TestServerClient( server );

            HttpResponseMessage bug = await client.Get( "?sayHello" );
            g.Dispose();

            var log = Directory.EnumerateFiles( Path.Combine( LogFile.RootLogPath, "GrandOutput_configuration_from_Json" ) ).Single();
            File.ReadAllText( log ).Should().Contain( "/?sayHello" );
        }


        //[Test]
        //public void grand_output_configuration_from_configurationmodel()
        //{
        //    using( var e = new ManualResetEvent( false ) )
        //    {
        //        var createHandler = GrandOutput.CreateHandler;
        //        try
        //        {
        //            var configSource = new MemoryConfigurationSource
        //            {
        //                InitialData = new Dictionary<string, string> { { "GrandOutput:TimerDuration", TimeSpan.FromSeconds( 10 ).ToString() } }
        //            };

        //            using( var client = new TestServerClient( CreateServerWithUseMonitoring( "GrandOutput", configSource, out IConfigurationRoot configRoot ) ) )
        //            {
        //                SystemActivityMonitor.RootLogPath.Should().NotBeNull().And.Contain( "Logs" );
        //                GrandOutput.Default.Should().NotBeNull();

        //                var configProvider = configRoot.Providers.OfType<MemoryConfigurationProvider>().First();
        //                configProvider.Set( "GrandOutput:TimerDuration", TimeSpan.FromSeconds( 2 ).ToString() );
        //                configProvider.Add( "GrandOutput:TextFile:Path", "Monitoring" );
        //                configProvider.Add( "GrandOutput:TextFile:MaxCountPerFile", "10" );

        //                int newHandlerCreated = 0;
        //                GrandOutput.CreateHandler = ( handlerConfig ) =>
        //                {
        //                    newHandlerCreated++;
        //                    handlerConfig.Should().BeOfType<TextFileConfiguration>();
        //                    TextFileConfiguration textFileConfiguration = (TextFileConfiguration)handlerConfig;
        //                    textFileConfiguration.MaxCountPerFile.Should().Be( 10 );
        //                    return createHandler( handlerConfig );
        //                };

        //                var section = configRoot.GetSection( "GrandOutput" );

        //                var reloadToken = section.GetReloadToken();
        //                reloadToken.RegisterChangeCallback( _ => e.Set(), null );
        //                configRoot.Reload();
        //                e.WaitOne( 200 );

        //                reloadToken.HasChanged.Should().BeTrue();
        //                newHandlerCreated.Should().Be( 1 );

        //                GrandOutput.CreateHandler = ( handlerConfig ) =>
        //                {
        //                    newHandlerCreated++;
        //                    handlerConfig.Should().BeOfType<BinaryFileConfiguration>();
        //                    BinaryFileConfiguration configuration = (BinaryFileConfiguration)handlerConfig;
        //                    configuration.MaxCountPerFile.Should().Be( 100 );
        //                    configuration.UseGzipCompression.Should().BeTrue();

        //                    return createHandler( handlerConfig );
        //                };

        //                configProvider.Set( "GrandOutput:BinaryFile:MaxCountPerFile", "100" );
        //                configProvider.Set( "GrandOutput:BinaryFile:UseGzipCompression", "True" );
        //                configRoot.Reload();

        //                newHandlerCreated.Should().Be( 2 );
        //            }
        //        }
        //        finally
        //        {
        //            GrandOutput.CreateHandler = createHandler;
        //        }
        //    }
        //}

        //[Test]
        //public void grand_output_configuration_create_handler_exception()
        //{
        //    var createHandler = GrandOutput.CreateHandler;
        //    try
        //    {
        //        GrandOutput.CreateHandler = ( handlerConfig ) =>
        //        {
        //            throw new Exception( "Ouin" );
        //            return createHandler( handlerConfig );
        //        };
        //        using( var client = new TestServerClient( CreateServerWithUseMonitoring( new GrandOutputOptions
        //        {
        //            TextFile = new TextFileConfiguration
        //            {
        //                Path = "Monitoring"
        //            }
        //        } ) ) )
        //        {
        //            var config = new GrandOutputConfiguration();
        //            config.AddHandler( new TextFileConfiguration { Path = "Monitoring" } );
        //            GrandOutput.EnsureActiveDefault( config );
        //        }
        //    }
        //    finally
        //    {
        //        GrandOutput.CreateHandler = createHandler;
        //    }
        //}

        /// <summary>
        /// Creates a TestServer with the GrandOutput.Default or witn a explicit instance.
        /// </summary>
        /// <param name="config">Configuration to use. Can be null.</param>
        /// <param name="grandOutput">Explicit instance (null for the GrandOutput.Default).</param>
        /// <param name="monitoringConfigurationPath">Path to the monitoring configuration.</param>
        /// <returns>The test server.</returns>
        static TestServer CreateServerWithUseMonitoring(
            IConfigurationSource config,
            GrandOutput grandOutput = null,
            string monitoringConfigurationPath = "Monitor" )
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
            if( config != null )
            {
                b.ConfigureAppConfiguration( ( ctx, configBuilder ) =>
                {
                    configBuilder.Add( config );
                } );
            }
            if( grandOutput == null )
            {
                b.UseMonitoring( monitoringConfigurationPath );
            }
            else
            {
                b.UseMonitoring( grandOutput, monitoringConfigurationPath );
            }
            return new TestServer( b );
        }

     }
}
