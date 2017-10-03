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
using System.Reflection;

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

            string logPath = Path.Combine( LogFile.RootLogPath, "GrandOutput_configuration_from_Json" );
            if( Directory.Exists( logPath ) ) Directory.Delete( logPath, true );

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

            var log = Directory.EnumerateFiles( logPath ).Single();
            File.ReadAllText( log ).Should().Contain( "/?sayHello" );
        }

        [Test]
        public async Task when_no_configuration_exists_the_default_is_a_Text_TextFile_handler_like_the_default_one_of_CK_Monitoring()
        {
            // Ensures that GrandOutput.Default is not working on
            // the LogFile.RootLogPath/Text default text handler.
            GrandOutput.EnsureActiveDefault( new GrandOutputConfiguration() );

            const string c = @"{ ""Monitor"": {
                                    ""GrandOutput"": {
                                        ""Handlers"": {
                                            ""TextFile"": {
                                                ""Path"": ""conf_before_default""
                                            }
                                        }
                                     }
                                  }
                               }";
            string logBefore = Path.Combine( LogFile.RootLogPath, "conf_before_default" );
            string logDefault = Path.Combine( LogFile.RootLogPath, "Text" );
            if( Directory.Exists( logBefore ) ) Directory.Delete( logBefore, true );
            if( Directory.Exists( logDefault ) ) Directory.Delete( logDefault, true );

            var config = new DynamicJsonConfigurationSource( c );
            using( var g = new GrandOutput( new GrandOutputConfiguration() ) )
            {
                Action<IActivityMonitor> autoRegisterer = m => g.EnsureGrandOutputClient( m );
                ActivityMonitor.AutoConfiguration += autoRegisterer;
                try
                {
                    using( var client = CreateServerWithUseMonitoring( config, g ) )
                    {
                        (await client.Get( "?sayHello&in_initial_config" )).Dispose();
                        config.Delete();
                        Thread.Sleep( 100 );
                        (await client.Get( "?sayHello&in_default_config" )).Dispose();
                    }
                }
                finally
                {
                    ActivityMonitor.AutoConfiguration -= autoRegisterer;
                }
            }

            var log1 = Directory.EnumerateFiles( logBefore ).Single();
            File.ReadAllText( log1 ).Should().Contain( "in_initial_config" )
                                             .And.NotContain( "in_default_config" );

            var log2 = Directory.EnumerateFiles( logDefault ).Single();
            File.ReadAllText( log2 ).Should().NotContain( "in_initial_config" )
                                             .And.Contain( "in_default_config" );
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

            string logPath1 = Path.Combine( LogFile.RootLogPath, "dynamic_conf_1" );
            string logPath2 = Path.Combine( LogFile.RootLogPath, "dynamic_conf_2" );
            string logPath3 = Path.Combine( LogFile.RootLogPath, "dynamic_conf_3" );
            if( Directory.Exists( logPath1 ) ) Directory.Delete( logPath1, true );
            if( Directory.Exists( logPath2 ) ) Directory.Delete( logPath2, true );
            if( Directory.Exists( logPath3 ) ) Directory.Delete( logPath3, true );

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
                        (await client.Get( "?sayHello&we_are_in_binary_in_config_2" )).Dispose();
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

            var log1 = Directory.EnumerateFiles( logPath1 ).Single();
            File.ReadAllText( log1 ).Should().Contain( "/?sayHello&WhileConfig_1" )
                                             .And.NotContain( "we_are_in_binary_in_config_2" )
                                             .And.NotContain( "/?sayHello&WhileConfig_3" );


            var log2 = Directory.EnumerateFiles( logPath2 ).Single();
            log2.Should().EndWith( ".ckmon" );
            PoorASCIIStringFromBytes( File.ReadAllBytes( log2 ) )
                    .Should().Contain( "we_are_in_binary_in_config_2" )
                    .And.NotContain( "?sayHello&WhileConfig_1" )
                    .And.NotContain( "/?sayHello&WhileConfig_3" );

            var log3 = Directory.EnumerateFiles( logPath3 ).Single();
            File.ReadAllText( log3 ).Should().Contain( "/?sayHello&WhileConfig_3" )
                                            .And.NotContain( "/?sayHello&WhileConfig_1" )
                                            .And.NotContain( "we_are_in_binary_in_config_2" );

        }

        static string PoorASCIIStringFromBytes( byte[] bytes )
        {
            return new String( bytes.Where( b => b > 8 && b < 127 ).Select( b => (char)b ).ToArray() );
        }


        [Test]
        public async Task hidden_async_bugs_aka_Task_UnobservedExceptions_are_handled_like_AppDomain_unhandled_exceptions_as_CriticalErrors()
        {
            const string c1 = @"{ ""Monitor"": {
                                    ""GrandOutput"": {
                                        ""HandleCriticalErrors"": true,
                                        ""Handlers"": {
                                            ""TextFile"": {
                                                ""Path"": ""unhandled_and_unobserved""
                                            }
                                        }
                                    }
                                 }
                              }";

            string logPath = Path.Combine( LogFile.RootLogPath, "unhandled_and_unobserved" );
            if( Directory.Exists( logPath ) ) Directory.Delete( logPath, true );

            var config = new DynamicJsonConfigurationSource( c1 );
            using( var g = new GrandOutput( new GrandOutputConfiguration() ) )
            {
                g.HandleCriticalErrors.Should().BeFalse();
                g.HandleCriticalErrors = true;
                Action<IActivityMonitor> autoRegisterer = m => g.EnsureGrandOutputClient( m );
                ActivityMonitor.AutoConfiguration += autoRegisterer;
                try
                {
                    using( var client = CreateServerWithUseMonitoring( config, g ) )
                    {
                        g.HandleCriticalErrors.Should().BeTrue();
                        (await client.Get( "?explicitCriticalError" )).Dispose();
                        // Unable to make this works:
                        // 1 - Task exceptions are raised loooooong after the error.
                        // 2 - Thread exceptions kills the process.
                        //(await client.Get( "?hiddenAsyncBug" )).Dispose();
                        //(await client.Get( "?unhandledAppDomainException" )).Dispose();

                        // Since the GrandOutput.Dispose is now correcly called thanks to IApplicationLifetime
                        // we have to wait a little bit for the critical error to be dispatched.
                        Thread.Sleep( 200 );
                    }
                }
                finally
                {
                    ActivityMonitor.AutoConfiguration -= autoRegisterer;
                }
            }

            var log = Directory.EnumerateFiles( logPath ).Single();
            File.ReadAllText( log ).Should()
                    .Contain( "I'm a Critical error." );
                    //.And.Contain( "I'm an horrible HiddenAsyncBug!" );
                    //.And.Contain( "I'm an unhandled exception." );
        }

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
