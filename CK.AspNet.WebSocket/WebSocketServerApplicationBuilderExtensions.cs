using System;
using CK.AspNet.WebSocket;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using CK.Core;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Provides the <see cref="UseWebSocketServer"/> extension method that mounts a message-dispatching
/// WebSocket endpoint as a plain middleware: no endpoint routing (no <c>UseRouting</c>) is required.
/// </summary>
public static class WebSocketServerApplicationBuilderExtensions
{
    /// <summary>
    /// Mounts a WebSocket endpoint on <paramref name="path"/>.
    /// <para>
    /// Requests on another path flow to the next middleware. A non WebSocket request on the path is
    /// answered with a 400. The path match is an exact <see cref="PathString"/> equality
    /// (case-insensitive, no trailing-slash tolerance).
    /// </para>
    /// <para>
    /// Each call adds its own WebSocket middleware instance; extra instances are inert but a host
    /// normally mounts its endpoints once.
    /// </para>
    /// </summary>
    /// <typeparam name="TMessageIn">Incoming message type.</typeparam>
    /// <typeparam name="TMessageOut">Outgoing message type.</typeparam>
    /// <param name="app">This application builder.</param>
    /// <param name="path">The endpoint path (e.g. <c>"/ws"</c>).</param>
    /// <param name="build">Configures the protocol and the dispatcher (both are required).</param>
    /// <param name="configureOptions">Optional dispatcher options (close timeout, buffer sizes...).</param>
    /// <returns>This application builder (fluent).</returns>
    public static IApplicationBuilder UseWebSocketServer<TMessageIn, TMessageOut>(
        this IApplicationBuilder app,
        PathString path,
        Action<MessageDispatcherBuilder<TMessageIn, TMessageOut>> build,
        Action<WebSocketConnectionDispatcherOptions>? configureOptions = null )
    {
        var dispatcher = app.ApplicationServices.GetRequiredService<WebSocketConnectionDispatcher>();

        var builder = new MessageDispatcherBuilder<TMessageIn, TMessageOut>();
        build( builder );
        builder.Validate();
        var messageDispatcher = builder.DispatcherFactory!( app.ApplicationServices );

        var options = new WebSocketConnectionDispatcherOptions();
        configureOptions?.Invoke( options );
        options.WebSockets.FramePackets = builder.IsEndOfMessageDelimited;

        var handler = new WebSocketConnectionHandler<TMessageIn, TMessageOut>( builder.Protocol!, messageDispatcher );

        app.UseWebSockets();
        var connectionDelegate = handler.OnConnectedAsync;
        app.Use( next => context =>
        {
            if( context.Request.Path != path ) return next( context );
            if( !context.WebSockets.IsWebSocketRequest )
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return context.Response.WriteAsync( "WebSocket request expected." );
            }

            var monitor = context.GetRequestMonitor();
            return dispatcher.ExecuteAsync( monitor, context, options, connectionDelegate );
        } );
        return app;
    }
}
