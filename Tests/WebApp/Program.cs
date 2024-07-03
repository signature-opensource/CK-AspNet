using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CK.Core;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WebApp
{
    public class Program
    {
        public static void Main( string[] args )
        {
            var host = Host.CreateDefaultBuilder( args )
                .ConfigureWebHostDefaults(
                    webHostBuilder =>
                    {
                        Throw.DebugAssert( "This is what the IWebHostBuiler.UseScopedHttpContext uses to detect taht it is used from a WebApplicationBuilder.",
                                           webHostBuilder is not ConfigureWebHostBuilder );
                        Throw.DebugAssert( "The old builder is internal.", webHostBuilder.GetType().Name == "GenericWebHostBuilder" );
                        webHostBuilder
                            .UseKestrel()
                            .UseScopedHttpContext()
                            .UseContentRoot( Directory.GetCurrentDirectory() )
                            .ConfigureLogging( b =>
                            {
                                b.SetMinimumLevel( Microsoft.Extensions.Logging.LogLevel.Trace );
                            } )
                            .ConfigureAppConfiguration( c => c.AddJsonFile( "appsettings.json", true, true ) )
                            .UseIISIntegration()
                            .UseStartup<Startup>();
                    } )
                .UseCKMonitoring()
                .Build();
            host.Run();
        }

    }
}
