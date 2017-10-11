using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WebApp
{
    public class Program
    {
        public static void Main( string[] args )
        {
            {
                var host = new WebHostBuilder()
                    .UseKestrel()
                    .UseContentRoot( Directory.GetCurrentDirectory() )
                    .ConfigureLogging( b =>
                    {
                        b.SetMinimumLevel( Microsoft.Extensions.Logging.LogLevel.Trace );
                    } )
                    .ConfigureAppConfiguration( c => c.AddJsonFile( "appsettings.json", true, true ) )
                    .UseMonitoring()
                    .UseIISIntegration()
                    .UseStartup<Startup>()
                    .Build();

                host.Run();
            }
        }

    }
}
