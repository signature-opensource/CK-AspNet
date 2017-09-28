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
using System.Text;

namespace CK.AspNet.Tester.Tests
{
    [TestFixture]
    public class GrandOutputWebHostTests
    {
        [SetUp]
        public void GrandOutput_Default_should_be_configured_with_default_values()
        {
            using( var client = CreateServerWithUseMonitoring( null ) )
            {
                LogFile.RootLogPath.Should().NotBeNull().And.EndWith( Path.DirectorySeparatorChar + "Logs" + Path.DirectorySeparatorChar );
                GrandOutput.Default.Should().NotBeNull();
            }
        }

        [Test]
        public async Task GrandOutput_configuration_with_a_text_log_from_Json()
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

            Directory.Delete( Path.Combine( LogFile.RootLogPath, "GrandOutput_configuration_from_Json" ), true );

            using( var g = new GrandOutput( new GrandOutputConfiguration() ) )
            {
                Action<IActivityMonitor> autoRegisterer = m => g.EnsureGrandOutputClient( m );
                ActivityMonitor.AutoConfiguration += autoRegisterer;
                try
                {
                    using( var client = CreateServerWithUseMonitoring( config, g ) )
                    {
                        (await client.Get( "?sayHello" )).Dispose();
                    }
                }
                finally
                {
                    ActivityMonitor.AutoConfiguration -= autoRegisterer;
                }
            }

            var log = Directory.EnumerateFiles( Path.Combine( LogFile.RootLogPath, "GrandOutput_configuration_from_Json" ) ).Single();
            File.ReadAllText( log ).Should().Contain( "/?sayHello" );
        }

        [Test]
        public async Task GrandOutput_dynamic_configuration_with_a_text_and_the_binary_and_then_text_log_from_Json()
        {
            const string c1 = @"{ ""Monitor"": {
                                    ""GrandOutput"": {
                                        ""Handlers"": {
                                            ""TextFile"": {
                                                ""Path"": ""dynamic_conf_1""
                                            }
                                        }
                                    }
                                 }
                              }";

            const string c2 = @"{ ""Monitor"": {
                                    ""GrandOutput"": {
                                        ""Handlers"": {
                                            ""BinaryFile"": {
                                                ""Path"": ""dynamic_conf_2""
                                            }
                                        }
                                    }
                                 }
                              }";

            const string c3 = @"{ ""Monitor"": {
                                    ""GrandOutput"": {
                                        ""Handlers"": {
                                            ""TextFile"": {
                                                ""Path"": ""dynamic_conf_3""
                                            }
                                        }
                                    }
                                 }
                              }";

            Directory.Delete( Path.Combine( LogFile.RootLogPath, "dynamic_conf_1" ), true );
            Directory.Delete( Path.Combine( LogFile.RootLogPath, "dynamic_conf_2" ), true );
            Directory.Delete( Path.Combine( LogFile.RootLogPath, "dynamic_conf_3" ), true );

            var config = new DynamicJsonConfigurationSource( c1 );
            using( var g = new GrandOutput( new GrandOutputConfiguration() ) )
            {
                Action<IActivityMonitor> autoRegisterer = m => g.EnsureGrandOutputClient( m );
                ActivityMonitor.AutoConfiguration += autoRegisterer;
                try
                {
                    using( var client = CreateServerWithUseMonitoring( config, g ) )
                    {
                        (await client.Get( "?sayHello&WhileConfig_1" )).Dispose();
                        config.SetJson( c2 );
                        Thread.Sleep( 100 );
                        (await client.Get( "?sayHello&NOSHOW_since_we_are_in_binary" )).Dispose();
                        config.SetJson( c3 );
                        Thread.Sleep( 100 );
                        (await client.Get( "?sayHello&WhileConfig_3" )).Dispose();
                    }
                }
                finally
                {
                    ActivityMonitor.AutoConfiguration -= autoRegisterer;
                }
            }

            var log1 = Directory.EnumerateFiles( Path.Combine( LogFile.RootLogPath, "dynamic_conf_1" ) ).Single();
            File.ReadAllText( log1 ).Should().Contain( "/?sayHello&WhileConfig_1" )
                                             .And.NotContain( "NOSHOW_since_we_are_in_binary" )
                                             .And.NotContain( "/?sayHello&WhileConfig_3" );


            var log2 = Directory.EnumerateFiles( Path.Combine( LogFile.RootLogPath, "dynamic_conf_2" ) ).Single();
            log2.Should().EndWith( ".ckmon" );
            PoorASCIIStringFromBytes( File.ReadAllBytes( log2 ) )
                    .Should().Contain( "NOSHOW_since_we_are_in_binary" )
                    .And.NotContain( "?sayHello&WhileConfig_1" )
                    .And.NotContain( "/?sayHello&WhileConfig_3" );

            var log3 = Directory.EnumerateFiles( Path.Combine( LogFile.RootLogPath, "dynamic_conf_3" ) ).Single();
            File.ReadAllText( log3 ).Should().Contain( "/?sayHello&WhileConfig_3" )
                                            .And.NotContain( "/?sayHello&WhileConfig_1" )
                                            .And.NotContain( "NOSHOW_since_we_are_in_binary" );


        }

        static string PoorASCIIStringFromBytes( byte[] bytes )
        {
            return new String( bytes.Where( b => b > 8 && b < 127 ).Select( b => (char)b ).ToArray() );
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
        /// Creates a TestServerClient with the GrandOutput.Default or witn a explicit instance.
        /// </summary>
        /// <param name="config">Configuration to use. Can be null.</param>
        /// <param name="grandOutput">Explicit instance (null for the GrandOutput.Default).</param>
        /// <param name="monitoringConfigurationPath">Path to the monitoring configuration.</param>
        /// <returns>The test server client.</returns>
        static TestServerClient CreateServerWithUseMonitoring(
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
                    app.UseRequestMonitor();
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
            return new TestServerClient( new TestServer( b ), disposeTestServer: true );
        }

     }
}
