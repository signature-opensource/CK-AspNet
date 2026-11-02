using CK.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.AppIdentity;

/// <summary>
/// Adds the services of the host to the services of a web application:
/// <list type="bullet">
///   <item>A singleton is forwarded to the host (one instance): a lazy factory. The web application's container is never
///   disposed (the feature is static, its lifetime is the host's), so it never disposes what the factory returns.</item>
///   <item>A singleton instance and an open generic are copied.</item>
///   <item>A scoped or transient service is copied: it is created in the scopes of the web application.</item>
///   <item>A container-configured service (<see cref="AutoServiceKind.IsContainerConfiguredService"/>) is copied, even a
///   singleton: each web application has its own instance.</item>
/// </list>
/// What the web application already registers (its infrastructure: configuration, environment, lifetime, server...)
/// is kept, except the <see cref="ILoggerFactory"/> that is the host's one (one logging pipeline).
/// </summary>
static class HostServiceForwarder
{
    // An IHostedService would start twice; the monitor of a request is the one of its ScopedHttpContext (registered by
    // CKBuild in the web application).
    static readonly HashSet<Type> _excluded = [typeof( IHostedService ), typeof( IActivityMonitor ), typeof( IParallelLogger )];

    // Taken from the host even if the web application has its own.
    static readonly HashSet<Type> _fromTheHost = [typeof( ILoggerFactory )];

    public static void Forward( IActivityMonitor monitor, IServiceCollection hostServices, IServiceProvider host, IServiceCollection services )
    {
        var map = host.GetService<IStObjMap>();
        foreach( var t in _fromTheHost ) services.RemoveAll( t );
        var own = new HashSet<Type>( services.Select( d => d.ServiceType ) );
        int forwarded = 0, copied = 0, containerConfigured = 0;
        var duplicated = new List<Type>();
        foreach( var g in hostServices.GroupBy( d => (d.ServiceType, d.ServiceKey) ) )
        {
            var t = g.Key.ServiceType;
            if( _excluded.Contains( t ) || own.Contains( t ) ) continue;
            var descriptors = g.ToList();
            if( IsContainerConfigured( map, t ) )
            {
                foreach( var d in descriptors ) services.Add( d );
                ++containerConfigured;
                continue;
            }
            if( t.IsGenericTypeDefinition || descriptors.Any( d => d.Lifetime != ServiceLifetime.Singleton ) )
            {
                // An open generic is closed by each container; scoped and transient services are created in the scopes of
                // the web application. A singleton among them would be a second instance.
                foreach( var d in descriptors )
                {
                    services.Add( d );
                    ++copied;
                    if( d.Lifetime == ServiceLifetime.Singleton && !t.IsGenericTypeDefinition && !HasInstance( d ) ) duplicated.Add( t );
                }
                continue;
            }
            if( descriptors.Count == 1 && HasInstance( descriptors[0] ) )
            {
                services.Add( descriptors[0] );
                ++copied;
                continue;
            }
            var key = g.Key.ServiceKey;
            if( key == null )
            {
                services.Add( new ServiceDescriptor( t, _ => host.GetRequiredService( t ), ServiceLifetime.Singleton ) );
                if( descriptors.Count > 1 )
                {
                    // The container of the web application would give the last one for each item: the enumeration
                    // is forwarded as a whole.
                    var tEnum = typeof( IEnumerable<> ).MakeGenericType( t );
                    services.Add( new ServiceDescriptor( tEnum, _ => host.GetRequiredService( tEnum ), ServiceLifetime.Singleton ) );
                }
            }
            else
            {
                if( descriptors.Count > 1 ) monitor.Warn( $"Keyed singleton '{t:C}' ('{key}') is registered {descriptors.Count} times: only the last one is forwarded." );
                services.Add( new ServiceDescriptor( t, key, ( _, k ) => host.GetRequiredKeyedService( t, k ), ServiceLifetime.Singleton ) );
            }
            ++forwarded;
        }
        foreach( var t in _fromTheHost )
        {
            services.Add( new ServiceDescriptor( t, _ => host.GetRequiredService( t ), ServiceLifetime.Singleton ) );
        }
        monitor.Trace( $"Host services: {forwarded} singletons forwarded, {containerConfigured} container-configured service types and {copied} other descriptors copied, {own.Count} service types of the web application kept." );
        if( duplicated.Count > 0 )
        {
            monitor.Warn( $"Singletons registered with scoped or transient services of the same type are copied (a second instance in the web application): {duplicated.Distinct().Select( t => t.ToCSharpName() ).Concatenate()}." );
        }

        static bool HasInstance( ServiceDescriptor d ) => d.IsKeyedService ? d.KeyedImplementationInstance != null : d.ImplementationInstance != null;

        static bool IsContainerConfigured( IStObjMap? map, Type t )
        {
            return map != null
                   && !t.IsGenericTypeDefinition
                   && map.ToLeaf( t ) is IStObjServiceClassDescriptor s
                   && (s.AutoServiceKind & AutoServiceKind.IsContainerConfiguredService) != 0;
        }
    }
}
