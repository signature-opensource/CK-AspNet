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
using Microsoft.AspNetCore.Builder;

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
            return DoUseMonitoring( builder, null, configurationPath );
        }

        class PostInstanciationFilter : IStartupFilter
        {
            readonly GrandOutputConfigurationInitializer _initalizer;

            public PostInstanciationFilter( GrandOutputConfigurationInitializer initalizer )
            {
                _initalizer = initalizer;
            }

            public Action<IApplicationBuilder> Configure( Action<IApplicationBuilder> next )
            {
                return builder =>
                {
                    var lifeTime = builder.ApplicationServices.GetRequiredService<IApplicationLifetime>();
                    _initalizer.PostInitialze( lifeTime );
                    next( builder );
                };
            }
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
            return DoUseMonitoring( builder, grandOutput, configurationPath );
        }

        static IWebHostBuilder DoUseMonitoring( IWebHostBuilder builder, GrandOutput grandOutput, string configurationPath )
        {
            // Three steps initialization:
            // First creates the initializer instance.
            var initializer = new GrandOutputConfigurationInitializer( grandOutput );
            builder.ConfigureLogging( ( ctx, l ) =>
            {
                var section = ctx.Configuration.GetSection( configurationPath );
                // Second, give it the environment and its section.
                initializer.Initialize( ctx.HostingEnvironment, section );
            } );
            // Now, registers the PostInstanciationFilter as a transient object.
            // This startup filter will inject the Applcation service IApplicationLifetime.
            builder.ConfigureServices( services => services.AddTransient<IStartupFilter>( _ => new PostInstanciationFilter( initializer ) ) );
            return builder;
        }
    }
}
