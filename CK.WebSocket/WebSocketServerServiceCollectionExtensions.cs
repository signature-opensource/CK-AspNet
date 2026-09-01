using CK.WebSocket;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides the <see cref="AddWebSocketServer(IServiceCollection)"/> extension method.
/// </summary>
public static class WebSocketServerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the singletons required by
    /// <c>UseWebSocketServer</c>: the connection manager and the connection dispatcher.
    /// Unlike SimpleR's <c>AddSimpleR</c>, this pulls no routing nor authorization services.
    /// </summary>
    /// <param name="services">This service collection.</param>
    /// <returns>This service collection (fluent).</returns>
    public static IServiceCollection AddWebSocketServer( this IServiceCollection services )
    {
        services.TryAddSingleton<WebSocketConnectionManager>();
        services.TryAddSingleton<WebSocketConnectionDispatcher>();
        return services;
    }
}
