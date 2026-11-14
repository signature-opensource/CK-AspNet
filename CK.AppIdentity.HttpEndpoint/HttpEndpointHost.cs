using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.AppIdentity;

/// <summary>
/// One instance per host: the services of the host (captured by <see cref="HttpEndpointHostRegistration"/> when the
/// map is registered) and the application's configurations of the endpoint (added by
/// <see cref="HttpEndpointHostApplicationBuilderExtensions.ConfigureHttpEndpoint"/>), in their order.
/// <para>
/// Both find or add the instance in the host's services: their order doesn't matter.
/// </para>
/// </summary>
[SingletonService]
public sealed class HttpEndpointHost
{
    readonly List<Action<WebApplicationBuilder>> _configurations;
    IServiceCollection? _hostServices;

    HttpEndpointHost()
    {
        _configurations = new List<Action<WebApplicationBuilder>>();
    }

    /// <summary>
    /// Gets the services of the host. Null until the map that contains the <see cref="HttpEndpointHostRegistration"/>
    /// has been registered.
    /// </summary>
    public IServiceCollection? HostServices => _hostServices;

    /// <summary>
    /// Gets the application's configurations of the endpoint, in their order.
    /// </summary>
    public IReadOnlyList<Action<WebApplicationBuilder>> Configurations => _configurations;

    internal void SetHostServices( IServiceCollection services ) => _hostServices = services;

    internal void AddConfiguration( Action<WebApplicationBuilder> configure ) => _configurations.Add( configure );

    internal static HttpEndpointHost FindOrAdd( IServiceCollection services )
    {
        var d = services.FirstOrDefault( d => d.ServiceType == typeof( HttpEndpointHost ) && !d.IsKeyedService && d.ImplementationInstance != null );
        if( d != null ) return (HttpEndpointHost)d.ImplementationInstance!;
        var host = new HttpEndpointHost();
        services.AddSingleton( host );
        return host;
    }
}