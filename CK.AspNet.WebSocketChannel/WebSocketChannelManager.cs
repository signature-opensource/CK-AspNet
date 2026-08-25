using CK.Core;
using CK.PerfectEvent;
using Microsoft.Extensions.Hosting;
using SimpleR;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading.Tasks;

namespace CK.AspNet.WebSocketChannel;

/// <summary>
/// Process singleton that owns every open <see cref="WebSocketChannelConnection"/> of the application.
/// <para>
/// There is one socket per client, and one dictionary of sockets in the process: this object. Features
/// do not open connections, they observe them through <see cref="ConnectionOpened"/> and
/// <see cref="ConnectionClosed"/>, keep whatever state they need keyed by connection identifier, and
/// push under their own topic.
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
    readonly PerfectEventSender<MessageReceivedEvent> _messageReceived = new();

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
    /// Raised for each message a client sends, once its envelope has been read. Handlers filter on
    /// <see cref="MessageReceivedEvent.Topic"/>: this is a shared socket, so a feature sees the traffic
    /// of the others and ignores it.
    /// <para>
    /// As long as nobody subscribes, incoming messages are not even read: the channel stays purely
    /// descending and costs nothing. Subscribing turns it on, and with it the caveat that
    /// <see cref="MessageReceivedEvent"/> carries nothing authenticated.
    /// </para>
    /// </summary>
    public PerfectEvent<MessageReceivedEvent> MessageReceived => _messageReceived.PerfectEvent;

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
    /// Sends a message under a topic to one connection.
    /// Does nothing when the connection is unknown or already closed: a push racing with a
    /// disconnection is normal, and an offline client is caught later, when it reconnects.
    /// </summary>
    /// <param name="connectionId">The target connection.</param>
    /// <param name="topic">The topic that routes the message on the client.</param>
    /// <param name="message">The payload, as a JSON value. It is embedded as-is, not escaped.</param>
    public ValueTask SendAsync( string connectionId, string topic, ReadOnlyMemory<byte> message )
    {
        return _connections.TryGetValue( connectionId, out var connection )
                ? SendAsync( connection, topic, message )
                : ValueTask.CompletedTask;
    }

    /// <summary>
    /// Sends a message under a topic to an already resolved connection: use this overload when the
    /// connection is at hand, to skip the lookup.
    /// </summary>
    /// <param name="connection">The target connection.</param>
    /// <param name="topic">The topic that routes the message on the client.</param>
    /// <param name="message">The payload, as a JSON value. It is embedded as-is, not escaped.</param>
    public ValueTask SendAsync( WebSocketChannelConnection connection, string topic, ReadOnlyMemory<byte> message )
    {
        Throw.CheckNotNullArgument( connection );
        Throw.CheckNotNullOrWhiteSpaceArgument( topic );
        return connection.WriteAsync( CreateEnvelope( topic, message ) );
    }

    // The envelope is the whole wire format: a topic to route on, and the payload untouched.
    // WriteRawValue embeds the payload as a JSON value, so a feature hands over the very same bytes it
    // used to write on its own socket. Validation is skipped: the payload comes from our own writers.
    static ReadOnlyMemory<byte> CreateEnvelope( string topic, ReadOnlyMemory<byte> message )
    {
        var buffer = new ArrayBufferWriter<byte>( message.Length + 32 );
        using( var writer = new Utf8JsonWriter( buffer ) )
        {
            writer.WriteStartObject();
            writer.WriteString( "topic", topic );
            writer.WritePropertyName( "message" );
            writer.WriteRawValue( message.Span, skipInputValidation: true );
            writer.WriteEndObject();
            writer.Flush();
        }
        return buffer.WrittenMemory;
    }

    // The first and only unenveloped message of a connection: the client needs its identifier to send
    // it back on the authenticated Cris channel, and it belongs to no topic.
    static ReadOnlyMemory<byte> CreateNegotiation( string connectionId )
    {
        var buffer = new ArrayBufferWriter<byte>( 64 );
        using( var writer = new Utf8JsonWriter( buffer ) )
        {
            writer.WriteStartObject();
            writer.WriteString( "connectionId", connectionId );
            writer.WriteEndObject();
            writer.Flush();
        }
        return buffer.WrittenMemory;
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

    internal async Task<bool> OnConnectedAsync( IWebsocketConnectionContext<ReadOnlyMemory<byte>> connection )
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

        await c.WriteAsync( CreateNegotiation( c.ConnectionId ) ).ConfigureAwait( false );
        // Safe: one faulty feature must not tear down a socket that the other features share.
        await _connectionOpened.SafeRaiseAsync( c.Monitor, c ).ConfigureAwait( false );
        return true;
    }

    internal Task OnMessageAsync( string connectionId, ReadOnlySequence<byte> input )
    {
        // Nobody listens: do not even look at the bytes. This is what keeps the descending-only case
        // free, and the reason RawMessageProtocol hands the sequence over without decoding it.
        if( !_messageReceived.HasHandlers ) return Task.CompletedTask;
        if( !_connections.TryGetValue( connectionId, out var c ) ) return Task.CompletedTask;

        string topic;
        ReadOnlyMemory<byte> message;
        try
        {
            using var doc = JsonDocument.Parse( input );
            topic = doc.RootElement.GetProperty( "topic" ).GetString()
                    ?? throw new JsonException( "Null topic." );
            // Copied out of the document, and therefore out of the pipe's buffers, before anything can
            // await: this is what lets a handler keep the payload.
            var buffer = new ArrayBufferWriter<byte>( 256 );
            using( var writer = new Utf8JsonWriter( buffer ) )
            {
                doc.RootElement.GetProperty( "message" ).WriteTo( writer );
                writer.Flush();
            }
            message = buffer.WrittenMemory;
        }
        catch( Exception ex )
        {
            // A client can send anything. Dropping the message is the only sane answer: throwing here
            // would tear down a socket that the other features are using.
            c.Monitor.Warn( "Ignored an incoming message that is not a {topic,message} envelope.", ex );
            return Task.CompletedTask;
        }

        // Safe: one faulty feature must not tear down a socket that the other features share.
        return _messageReceived.SafeRaiseAsync( c.Monitor, new MessageReceivedEvent( c, topic, message ) );
    }

    internal Task OnDisconnectedAsync( string connectionId, Exception? exception )
    {
        return _connections.TryRemove( connectionId, out var c )
                ? CloseAsync( c, exception )
                : Task.CompletedTask;
    }

    // Disposes before raising, so that a send attempted from a handler is a silent no-op instead of a
    // write onto a socket that is already gone, whichever overload the handler uses.
    async Task CloseAsync( WebSocketChannelConnection c, Exception? exception )
    {
        await c.DisposeAsync().ConfigureAwait( false );
        await _connectionClosed.SafeRaiseAsync( c.Monitor, new ConnectionClosedEvent( c.ConnectionId, exception ) )
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
                await CloseAsync( c, null ).ConfigureAwait( false );
            }
        }
    }
}
