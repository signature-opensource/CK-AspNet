using CK.Core;
using CK.PerfectEvent;
using Microsoft.Extensions.Hosting;
using CK.WebSocket;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace CK.AspNet.WebSocketChannel;

/// <summary>
/// Process singleton that owns every open <see cref="WebSocketChannelConnection"/> of the application.
/// <para>
/// There is one socket per client, and one dictionary of sockets in the process: this object. Features
/// do not open connections, they observe them through <see cref="ConnectionOpened"/> and
/// <see cref="ConnectionClosed"/>, keep whatever state they need keyed by connection identifier, or by
/// the connection object itself, which stays harmless once closed, and push under their own topic on the
/// connection (<see cref="WebSocketChannelConnection.WriteAsync(string, ReadOnlyMemory{byte})"/>)
/// or to all of them at once (<see cref="SendBroadcastAsync(string, ReadOnlyMemory{byte})"/>).
/// </para>
/// <para>
/// This manager knows nothing about any feature: no identity, no domain, no business index. That is
/// deliberate, and it is what lets an arbitrary number of features share one socket without knowing
/// about each other.
/// </para>
/// </summary>
public sealed class WebSocketChannelManager : IRealObject
{
    readonly ConcurrentDictionary<string, WebSocketChannelConnection> _connections = new();
    readonly PerfectEventSender<WebSocketChannelConnection> _connectionOpened = new();
    readonly PerfectEventSender<ConnectionClosedEvent> _connectionClosed = new();
    readonly PerfectEventSender<MessageReceivedEvent> _allMessagesReceived = new();

    // Set by AbortAll on ApplicationStopping: once stopping, new connections are refused so an
    // auto-reconnecting client cannot re-arm the full ShutdownTimeout drain.
    volatile bool _stopping;

    /// <summary>
    /// Raised once a connection is open and has been told its identifier. A feature that needs to
    /// prepare per-connection state eagerly binds here; one that can wait for its first command does
    /// not have to.
    /// </summary>
    public PerfectEvent<WebSocketChannelConnection> ConnectionOpened => _connectionOpened.PerfectEvent;

    /// <summary>
    /// Raised once a connection is gone. The connection is already removed from this manager and
    /// disposed: handlers are here to release what they keyed by
    /// <see cref="ConnectionClosedEvent.ConnectionId"/>.
    /// </summary>
    public PerfectEvent<ConnectionClosedEvent> ConnectionClosed => _connectionClosed.PerfectEvent;

    /// <summary>
    /// Raised for each message any client sends, once its envelope has been read. Handlers filter on
    /// <see cref="MessageReceivedEvent.Topic"/>: this is a shared socket, so a feature sees the traffic
    /// of the others and ignores it. A feature that works per connection subscribes to
    /// <see cref="WebSocketChannelConnection.MessageReceived"/> instead, whose handlers run first. A
    /// handler that throws is logged and swallowed (the raise is safe): only the remaining synchronous
    /// handlers of this event are skipped, and <see cref="WebSocketChannelConnection.MessageReceived"/>
    /// is still raised.
    /// <para>
    /// Topics live in one flat namespace shared by every feature, so a topic must be globally unique:
    /// name it after your package.
    /// Anything shorter eventually collides, and a collision means another feature's payloads reaching
    /// your handler - silently, since nothing here can tell the two apart.
    /// </para>
    /// <para>
    /// As long as nobody subscribes, here nor on any connection, incoming messages are not even read: the
    /// channel stays purely descending and costs nothing. Subscribing turns it on, and with it the caveat
    /// that <see cref="MessageReceivedEvent"/> carries nothing authenticated.
    /// </para>
    /// </summary>
    public PerfectEvent<MessageReceivedEvent> AllMessagesReceived => _allMessagesReceived.PerfectEvent;

    /// <summary>
    /// Gets the number of currently open connections.
    /// </summary>
    public int Count => _connections.Count;

    /// <summary>
    /// Tries to obtain an open connection by its identifier.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="connection">The connection on success.</param>
    /// <returns>True if the connection is open, false otherwise.</returns>
    public bool TryGetConnection( string connectionId, [NotNullWhen( true )] out WebSocketChannelConnection? connection )
    {
        return _connections.TryGetValue( connectionId, out connection );
    }

    /// <summary>
    /// Writes the same frame to every open connection, in parallel. The frame must be an envelope built
    /// by <see cref="WebSocketChannelEnvelope.Create"/>: prefer
    /// <see cref="SendBroadcastAsync(string, ReadOnlyMemory{byte})"/>, which does exactly that.
    /// <para>
    /// A connection that opens during the broadcast may or may not receive it, and one that closed is
    /// silently skipped. Per-connection order is preserved: two broadcasts awaited one after the other
    /// reach every client in that order. An exception from one write propagates, as it does for a single
    /// <see cref="WebSocketChannelConnection.WriteAsync(ReadOnlyMemory{byte})"/>.
    /// </para>
    /// </summary>
    /// <param name="rawMessage">The frame to write, as it is.</param>
    public Task SendBroadcastAsync( ReadOnlyMemory<byte> rawMessage )
    {
        // Enumerating the dictionary itself is lock-free (unlike .Values, which snapshots under lock).
        var writes = new List<Task>();
        foreach( var kv in _connections )
        {
            writes.Add( kv.Value.WriteAsync( rawMessage ).AsTask() );
        }
        return Task.WhenAll( writes );
    }

    /// <summary>
    /// Sends a message under a topic to every open connection. The envelope is built once.
    /// </summary>
    /// <param name="topic">The topic that routes the message on the client. Must not be null or white space.</param>
    /// <param name="message">The payload, as a JSON value. It is embedded as-is, not escaped.</param>
    public Task SendBroadcastAsync( string topic, ReadOnlyMemory<byte> message )
    {
        return SendBroadcastAsync( WebSocketChannelEnvelope.Create( topic, message ) );
    }

    /// <summary>
    /// Registers <see cref="AbortAll"/> on <see cref="IHostApplicationLifetime.ApplicationStopping"/> so
    /// open connections are aborted before Kestrel starts draining (OnHostStopAsync is only a late
    /// backstop).
    /// </summary>
    void OnHostStart( IActivityMonitor monitor, IHostApplicationLifetime lifetime )
    {
        // This real object is a process singleton: reset _stopping so the manager is reusable if a new
        // host starts on the same StObjMap (e.g. across tests) after a previous host stopped.
        _stopping = false;
        lifetime.ApplicationStopping.Register( AbortAll );
    }

    internal async Task<bool> OnConnectedAsync( IActivityMonitor monitor, IWebSocketConnectionContext<ReadOnlyMemory<byte>> connection )
    {
        // Refuse new connections once stopping so a reconnect cannot re-arm the ShutdownTimeout drain.
        if( _stopping )
        {
            connection.Abort();
            return false;
        }

        var c = new WebSocketChannelConnection( connection );
        if( _connections.TryAdd( connection.ConnectionId, c ) is false )
        {
            await c.DisposeAsync().ConfigureAwait( false );
            return false;
        }

        // Re-check: AbortAll sets _stopping before iterating, so it may have missed this entry added
        // just after.
        if( _stopping && _connections.TryRemove( connection.ConnectionId, out _ ) )
        {
            connection.Abort();
            await c.DisposeAsync().ConfigureAwait( false );
            return false;
        }

        c.RegisterMessageReceivedRelay( _allMessagesReceived );
        await c.WriteNegotiationAsync().ConfigureAwait( false );
        // Safe: one faulty feature must not tear down a socket that the other features share.
        await _connectionOpened.SafeRaiseAsync( monitor, c ).ConfigureAwait( false );
        return true;
    }

    internal async Task OnMessageAsync( IActivityMonitor monitor, string connectionId, ReadOnlySequence<byte> input )
    {
        if( !_connections.TryGetValue( connectionId, out var c ) ) return;
        // Nobody listens, neither on this connection nor here: do not even look at the bytes. This is
        // what keeps the descending-only case free, and the reason RawMessageProtocol hands the
        // sequence over without decoding it.
        if( !c.HasMessageHandlers && !_allMessagesReceived.HasHandlers ) return;

        string topic;
        ReadOnlyMemory<byte> message;
        try
        {
            (topic, message) = WebSocketChannelEnvelope.Read( input );
        }
        catch( Exception ex )
        {
            // A client can send anything. Dropping the message is the only sane answer: throwing here
            // would tear down a socket that the other features are using.
            monitor.Warn( "Ignored an incoming message that is not a {topic,message} envelope.", ex );
            return;
        }

        var e = new MessageReceivedEvent( c, topic, message );
        await c.RaiseMessageReceivedAsync( monitor, e ).ConfigureAwait( false );
    }


    internal Task OnDisconnectedAsync( IActivityMonitor monitor, string connectionId, Exception? exception )
    {
        return _connections.TryRemove( connectionId, out var c )
                ? CloseAsync( monitor, c, exception )
                : Task.CompletedTask;
    }

    // Disposes before raising, so that a send attempted from a handler is a silent no-op instead of a
    // write onto a socket that is already gone, whichever overload the handler uses.
    async Task CloseAsync( IActivityMonitor monitor, WebSocketChannelConnection c, Exception? exception )
    {
        await c.DisposeAsync().ConfigureAwait( false );
        await _connectionClosed.SafeRaiseAsync( monitor, new ConnectionClosedEvent( c.ConnectionId, exception ) )
                               .ConfigureAwait( false );
    }

    /// <summary>
    /// Sets the stopping flag then aborts every tracked connection (registered on ApplicationStopping
    /// by <see cref="OnHostStart"/>). Each connection is then removed by the disconnect path as its
    /// read loop ends.
    /// </summary>
    internal void AbortAll()
    {
        _stopping = true;
        foreach( var kv in _connections )
        {
            kv.Value.Abort();
        }
    }

    async Task OnHostStopAsync( IActivityMonitor monitor )
    {
        // Late backstop: AbortAll should already have closed everything. Abort (not just dispose) any
        // straggler, since disposing alone never aborts the socket.
        _stopping = true;
        foreach( var kv in _connections )
        {
            if( _connections.TryRemove( kv.Key, out var c ) )
            {
                c.Abort();
                await CloseAsync( monitor, c, null ).ConfigureAwait( false );
            }
        }
    }
}
