using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.AppIdentity;

/// <summary>
/// One instance per host: the services of the host (captured by <see cref="HttpEndpointHostRegistration"/> when the
/// map is registered) and the application's configurations of the endpoints (added by
/// <see cref="HttpEndpointHostApplicationBuilderExtensions.AddHttpEndpoint"/>), by endpoint name.
/// <para>
/// Both find or add the instance in the host's services: their order doesn't matter.
/// </para>
/// </summary>
[SingletonService]
public sealed class HttpEndpointHost
{
    readonly Dictionary<string, Action<WebApplicationBuilder>?> _configurations;
    IServiceCollection? _hostServices;

    HttpEndpointHost()
    {
        _configurations = new Dictionary<string, Action<WebApplicationBuilder>?>( StringComparer.OrdinalIgnoreCase );
    }

    /// <summary>
    /// Gets the services of the host. Null until the map that contains the <see cref="HttpEndpointHostRegistration"/>
    /// has been registered.
    /// </summary>
    public IServiceCollection? HostServices => _hostServices;

    /// <summary>
    /// Gets the application's configurations of the endpoints, by endpoint name.
    /// </summary>
    public IReadOnlyDictionary<string, Action<WebApplicationBuilder>?> Configurations => _configurations;

    internal void SetHostServices( IServiceCollection services ) => _hostServices = services;

    internal void AddConfiguration( string name, Action<WebApplicationBuilder>? configure )
    {
        if( !_configurations.TryAdd( name, configure ) )
        {
            Throw.InvalidOperationException( $"The HttpEndpoint '{name}' is already configured." );
        }
    }

    internal static HttpEndpointHost FindOrAdd( IServiceCollection services )
    {
        var d = services.FirstOrDefault( d => d.ServiceType == typeof( HttpEndpointHost ) && !d.IsKeyedService && d.ImplementationInstance != null );
        if( d != null ) return (HttpEndpointHost)d.ImplementationInstance!;
        var host = new HttpEndpointHost();
        services.AddSingleton( host );
        return host;
    }
}
