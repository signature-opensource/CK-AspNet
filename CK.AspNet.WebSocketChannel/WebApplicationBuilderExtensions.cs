using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Buffers;

namespace CK.AspNet.WebSocketChannel;

/// <summary>
/// Extension methods that register and mount the one WebSocket endpoint of the application.
/// </summary>
public static class WebApplicationBuilderExtensions
{
    /// <summary>
    /// The WebSocket endpoint of the channel. The client must use the very same path.
    /// </summary>
    public const string DefaultPath = "/ws";

    /// <summary>
    /// Registers the CK.WebSocket services required by the channel.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The <paramref name="builder"/> for chaining.</returns>
    public static WebApplicationBuilder AddWebSocketChannel( this WebApplicationBuilder builder )
    {
        builder.Services.AddWebSocketServer();
        return builder;
    }

    /// <summary>
    /// Mounts the WebSocket endpoint of the channel as a plain middleware: no endpoint routing
    /// (no <c>UseRouting</c>) is required. Call this once: every feature shares this endpoint
    /// and is routed by topic, so there is nothing to mount per feature.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="path">The endpoint path. Defaults to <see cref="DefaultPath"/>.</param>
    /// <returns>The <paramref name="app"/> for chaining.</returns>
    public static IApplicationBuilder UseWebSocketChannel( this IApplicationBuilder app, string path = DefaultPath )
    {
        return app.UseWebSocketServer<ReadOnlySequence<byte>, ReadOnlyMemory<byte>>( path, b =>
        {
            b.UseEndOfMessageDelimitedProtocol( new RawMessageProtocol() );
            b.UseDispatcher<WebSocketChannelDispatcher>();
        },
        // Aborting (on ApplicationStopping) still triggers a graceful close that waits CloseTimeout
        // (5s) for the client handshake; zero tears the socket down at once so shutdown never
        // depends on client behaviour.
        options => options.WebSockets.CloseTimeout = TimeSpan.Zero );
    }
}
