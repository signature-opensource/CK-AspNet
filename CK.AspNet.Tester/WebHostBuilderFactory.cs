using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace CK.AspNet.Tester
{

    /// <summary>
    /// Sets of helpers tho build a <see cref="IWebHostBuilder"/>.
    /// </summary>
    public static class WebHostBuilderFactory
    {
        /// <summary>
        /// Creates a web host builder.
        /// </summary>
        /// <param name="startupType">Optional type of the startup ovject. Can be null.</param>
        /// <param name="contentRoot">Optional path of the content root. Can be null.</param>
        /// <param name="configureServices">Optional service configurator. Can be null.</param>
        /// <param name="configureApplication">Optional application configurator. Can be null.</param>
        /// <returns>The web host builder.</returns>
        public static IWebHostBuilder Create(
            Type startupType,
            string contentRoot,
            Action<IServiceCollection> configureServices,
            Action<IApplicationBuilder> configureApplication )
        {
            return Create( startupType, contentRoot, new[] { configureServices }, new[] { configureApplication } );
        }

        /// <summary>
        /// Creates a web host builder with multiple service and application configurators.
        /// </summary>
        /// <param name="startupType">Optional type of the startup ovject. Can be null.</param>
        /// <param name="contentRoot">Optional path of the content root. Can be null.</param>
        /// <param name="configureServices">Optional service configurators. Can be null.</param>
        /// <param name="configureApplication">Optional application configurators. Can be null.</param>
        /// <returns>The web host builder.</returns>
        public static IWebHostBuilder Create(
            Type startupType,
            string contentRoot,
            IEnumerable<Action<IServiceCollection>> configureServices,
            IEnumerable<Action<IApplicationBuilder>> configureApplication )
        {
            object startup = null;
            var webHostBuilder = new WebHostBuilder();
            if( contentRoot != null ) webHostBuilder.UseContentRoot( contentRoot );
            webHostBuilder.UseEnvironment( EnvironmentName.Development );
            webHostBuilder.ConfigureServices( services =>
               {
                   if( startupType != null )
                   {
                       startup = CreateStartupObject( startupType, services );
                   }
                   ConfigureServices( startup, services, configureServices );
               } )
              .Configure( builder =>
               {
                   ConfigureApplication( startup, builder, configureApplication );
               } );
            return webHostBuilder;
        }

        static object CreateStartupObject( Type startupType, IServiceCollection services )
        {
            Debug.Assert( startupType != null );
            object startup;
            var hostingEnvironment = ConfigureHostingEnvironment( startupType, services );
            int ctorCount = 0;
            var ctor = startupType.GetTypeInfo()
                                      .DeclaredConstructors
                                      .Select( c => new
                                      {
                                          Ctor = c,
                                          Params = c.GetParameters().Select( p => p.ParameterType ).ToArray(),
                                      } )
                                      .Select( c => { ++ctorCount; return c; } )
                                      .OrderByDescending( c => c.Params.Length )
                                      .Select( c => new
                                      {
                                          Ctor = c.Ctor,
                                          Params = c.Params,
                                          Values = c.Params
                                                      .Select( p => services.FirstOrDefault( s => p.IsAssignableFrom( s.ServiceType ) ) )
                                                      .Select( s => s?.ImplementationInstance )
                                      } )
                                      .Where( c => c.Values.All( v => v != null ) )
                                      .FirstOrDefault();
            if( ctorCount > 0 && ctor == null ) throw new Exception( $"Unable to find a constructor (out of {ctorCount}) with injectable parameters." );
            if( ctor == null ) startup = Activator.CreateInstance( startupType );
            else startup = Activator.CreateInstance( startupType, ctor.Values.ToArray() );
            return startup;
        }

        static IHostingEnvironment ConfigureHostingEnvironment( Type startup, IServiceCollection services )
        {
            Func<ServiceDescriptor, bool> isHostingEnvironmet = service => service.ImplementationInstance is IHostingEnvironment;
            var hostingEnvironment = (IHostingEnvironment)services.Single( isHostingEnvironmet ).ImplementationInstance;
            var assembly = startup.GetTypeInfo().Assembly;
            hostingEnvironment.ApplicationName = assembly.GetName().Name;
            return hostingEnvironment;
        }

        static void ConfigureServices(
            object startup,
            IServiceCollection services,
            IEnumerable<Action<IServiceCollection>> configureServices )
        {
            if( startup != null )
            {
                var conf = startup.GetType().GetMethod( "ConfigureServices" );
                conf?.Invoke( startup, new[] { services } );
            }
            if( configureServices != null )
            {
                foreach( var serviceConfiguration in configureServices )
                {
                    serviceConfiguration?.Invoke( services );
                }
            }
        }

        static void ConfigureApplication(
            object startup,
            IApplicationBuilder builder,
            IEnumerable<Action<IApplicationBuilder>> configureApplication )
        {
            if( configureApplication != null )
            {
                foreach( var applicationConfiguration in configureApplication )
                {
                    applicationConfiguration?.Invoke( builder );
                }
            }
            if( startup != null )
            {
                var conf = startup.GetType().GetMethod( "Configure" );
                conf?.Invoke( startup, new[] { builder } );
            }
        }

    }
}
