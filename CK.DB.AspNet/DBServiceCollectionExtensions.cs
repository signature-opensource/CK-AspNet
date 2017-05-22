using CK.Core;
using Microsoft.Extensions.DependencyInjection;
using System;
using CK.SqlServer.Setup;

namespace CK.AspNet
{
    /// <summary>
    /// Adds extension methods on <see cref="IServiceCollection"/>.
    /// Since the extension methods here do not conflict with more generic methods, the namespace is
    /// CK.AspNet to avoid cluttering the namespace names.
    /// </summary>
    public static class DBServiceCollectionExtensions
    {
        /// <summary>
        /// Registers all the StObj mappings from the default context of an assembly and also registers the <see cref="IStObjMap"/>.
        /// </summary>
        /// <param name="services">This services.</param>
        /// <param name="assemblyName">The assembly name.</param>
        /// <param name="defaultConnectionString">The connection string to override in the <see cref="SqlDefaultDatabase"/>.</param>
        /// <returns>This services collection.</returns>
        public static IServiceCollection AddDefaultStObjMap(this IServiceCollection services, string assemblyName, string defaultConnectionString = null)
        {
            var map = StObjContextRoot.Load(assemblyName);
            if (map == null)
                throw new ArgumentException($"The assembly {assemblyName} was not found or is not a valid StObj map assembly");

            if (!String.IsNullOrEmpty(defaultConnectionString))
            {
                var db = map.Default.Obtain<SqlDefaultDatabase>();
                db.ConnectionString = defaultConnectionString;
            }
            return AddStObjMap(services, map.Default);
        }

        /// <summary>
        /// Registers all the StObj mappings from a StObj context and also registers the <see cref="IStObjMap"/>.
        /// </summary>
        /// <param name="services">This services.</param>
        /// <param name="map">Contextual StObj objects to register.</param>
        /// <returns>This services collection.</returns>
        public static IServiceCollection AddStObjMap(this IServiceCollection services, IContextualStObjMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            foreach (var kv in map.Mappings)
            {
                services.AddSingleton(kv.Key, kv.Value);
            }
            services.AddSingleton(map.AllContexts);
            return services;
        }

    }
}
