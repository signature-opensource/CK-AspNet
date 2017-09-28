using CK.Core;
using CK.Monitoring;
using Microsoft.AspNetCore.Hosting;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Diagnostics;
using CK.AspNet;
using CK.Monitoring.Handlers;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Hosting
{

    /// <summary>
    /// Adds extension methods on <see cref="IWebHostBuilder"/>.
    /// </summary>
    public static class WebHostBuilderCKAspNetExtensions
    {
        /// <summary>
        /// Uses <see cref="CK.Monitoring"/> during the web host building and initializes the default <see cref="GrandOutput"/>,
        /// and bounds the configuration from the given configuration section.
        /// </summary>
        /// <param name="builder">Web host builder</param>
        /// <param name="configurationPath">The path of the monitoring configuration in the global configuration.</param>
        /// <returns>The builder.</returns>
        public static IWebHostBuilder UseMonitoring( this IWebHostBuilder builder, string configurationPath = "Monitoring" )
        {
            builder.ConfigureLogging( ( ctx, l ) =>
            {
                var section = ctx.Configuration.GetSection( configurationPath );
                new GrandOutputDefaultConfigurationInitializer( ctx.HostingEnvironment, section, null );
            } );
            return builder;
        }

        /// <summary>
        /// Uses <see cref="CK.Monitoring"/> during the web host building and initializes an instance of the <see cref="GrandOutput"/>
        /// that must not be null nor be the <see cref="GrandOutput.Default"/> and bounds the configuration from the given configuration section.
        /// </summary>
        /// <param name="builder">Web host builder</param>
        /// <param name="grandOutput">The target <see cref="GrandOutput"/>.</param>
        /// <param name="configurationPath">The path of the monitoring configuration in the global configuration.</param>
        /// <returns>The builder.</returns>
        public static IWebHostBuilder UseMonitoring( this IWebHostBuilder builder, GrandOutput grandOutput, string configurationPath = "Monitoring" )
        {
            if( grandOutput == null ) throw new ArgumentNullException( nameof( grandOutput ) );
            if( grandOutput == GrandOutput.Default ) throw new ArgumentException( "The GrandOutput must not be the default one.", nameof( grandOutput ) );
            builder.ConfigureLogging( ( ctx, l ) =>
            {
                var section = ctx.Configuration.GetSection( configurationPath );
                new GrandOutputDefaultConfigurationInitializer( ctx.HostingEnvironment, section, grandOutput );
            } );
            return builder;
        }


    }
}
