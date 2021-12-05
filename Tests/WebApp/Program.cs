using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
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
            {
                var host = Host.CreateDefaultBuilder( args )
                    .ConfigureWebHostDefaults(
                        webHostBuilder => webHostBuilder
                        .UseKestrel()
                        .UseContentRoot( Directory.GetCurrentDirectory() )
                        .ConfigureLogging( b =>
                        {
                            b.SetMinimumLevel( LogLevel.Trace );
                        } )
                        .ConfigureAppConfiguration( c => c.AddJsonFile( "appsettings.json", true, true ) )
                        .UseIISIntegration()
                        .UseStartup<Startup>()
                    )
                    .UseCKMonitoring()
                    .Build();
                host.Run();
            }
        }

    }
}
