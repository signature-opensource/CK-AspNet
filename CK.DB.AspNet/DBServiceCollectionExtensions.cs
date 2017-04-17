using CK.Core;
using Microsoft.Extensions.DependencyInjection;
using System;

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
        /// Registers all the StObj mappings from the default context of an assembly.
        /// </summary>
        /// <param name="services">This services.</param>
        /// <param name="assemblyName">The assembly name.</param>
        /// <returns>This services collection.</returns>
        public static IServiceCollection AddDefaultStObjMap(this IServiceCollection services, string assemblyName)
        {
            return AddStObjMap(services, StObjContextRoot.Load(assemblyName).Default);
        }

        /// <summary>
        /// Registers all the StObj mappings from a StObj context.
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
            return services;
        }

    }
}
