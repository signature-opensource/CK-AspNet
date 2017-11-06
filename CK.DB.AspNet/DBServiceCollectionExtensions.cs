using CK.Core;
using System;
using CK.SqlServer.Setup;
using System.Reflection;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Adds extension methods on <see cref="IServiceCollection"/>.
    /// Since the extension methods here do not conflict with more generic methods, the namespace is
    /// CK.AspNet to avoid cluttering the namespace names.
    /// </summary>
    public static class DBServiceCollectionExtensions
    {
        /// <summary>
        /// Registers all the StObj mappings from the default context of an assembly and also
        /// registers the <see cref="IStObjMap"/>.
        /// </summary>
        /// <param name="services">This services.</param>
        /// <param name="assemblyName">The assembly name.</param>
        /// <param name="defaultConnectionString">
        /// Optional connection string that will override <see cref="SqlDefaultDatabase"/> <see cref="SqlDatabase.ConnectionString">ConnectionString</see>.
        /// </param>
        /// <returns>This services collection.</returns>
        public static IServiceCollection AddDefaultStObjMap( this IServiceCollection services, Assembly stobjAssembly, string defaultConnectionString = null )
        {
            if( stobjAssembly == null )
            {
                throw new ArgumentNullException( nameof( stobjAssembly ) );
            }

            var map = StObjContextRoot.Load( stobjAssembly );
            if( map == null )
                throw new ArgumentException( $"The assembly {stobjAssembly.FullName} was not found or is not a valid StObj map assembly" );

            if( !String.IsNullOrEmpty( defaultConnectionString ) )
            {
                var db = map.Default.Obtain<SqlDefaultDatabase>();
                db.ConnectionString = defaultConnectionString;
            }
            return AddStObjMap( services, map.Default );
        }

        /// <summary>
        /// Registers all the StObj mappings from the default context of an assembly and also
        /// registers the <see cref="IStObjMap"/>.
        /// </summary>
        /// <param name="services">This services.</param>
        /// <param name="assemblyName">The assembly name.</param>
        /// <param name="defaultConnectionString">
        /// Optional connection string that will override <see cref="SqlDefaultDatabase"/> <see cref="SqlDatabase.ConnectionString">ConnectionString</see>.
        /// </param>
        /// <remarks>
        /// On NetCore runtime,  Assembly.LoadFrom is used to resolves the assembly from its full path.
        /// </remarks>
        /// <returns>This services collection.</returns>
        public static IServiceCollection AddDefaultStObjMap( this IServiceCollection services, string assemblyName, string defaultConnectionString = null )
        {
#if NET461
            return services.AddDefaultStObjMap( new AssemblyName( assemblyName ), defaultConnectionString );
#else
            string path = System.IO.Path.Combine( AppDomain.CurrentDomain.BaseDirectory, assemblyName + ".dll" );
            return services.AddDefaultStObjMap( Assembly.LoadFrom( path ), defaultConnectionString );
#endif
        }

        /// <summary>
        /// Registers all the StObj mappings from the default context of an assembly and also
        /// registers the <see cref="IStObjMap"/>.
        /// </summary>
        /// <param name="services">This services.</param>
        /// <param name="assemblyName">The assembly name.</param>
        /// <param name="defaultConnectionString">
        /// Optional connection string that will override <see cref="SqlDefaultDatabase"/> <see cref="SqlDatabase.ConnectionString">ConnectionString</see>.
        /// </param>
        /// <returns>This services collection.</returns>
        public static IServiceCollection AddDefaultStObjMap( this IServiceCollection services, AssemblyName assemblyName, string defaultConnectionString = null )
        {
            return services.AddDefaultStObjMap( Assembly.Load( assemblyName ), defaultConnectionString );
        }

        /// <summary>
        /// Registers all the StObj mappings from a StObj context and also registers the <see cref="IStObjMap"/>.
        /// </summary>
        /// <param name="services">This services.</param>
        /// <param name="map">Contextual StObj objects to register.</param>
        /// <returns>This services collection.</returns>
        public static IServiceCollection AddStObjMap( this IServiceCollection services, IContextualStObjMap map )
        {
            if( map == null ) throw new ArgumentNullException( nameof( map ) );
            foreach( var kv in map.Mappings )
            {
                services.AddSingleton( kv.Key, kv.Value );
            }
            services.AddSingleton( map.AllContexts );
            return services;
        }

    }
}
