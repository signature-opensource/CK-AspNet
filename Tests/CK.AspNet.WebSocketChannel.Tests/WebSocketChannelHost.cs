using CK.Core;
using CK.PerfectEvent;
using CK.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using static CK.Testing.MonitorTestHelper;

namespace CK.AspNet.WebSocketChannel.Tests;

/// <summary>
/// A running party exposing the channel on its endpoint (the "HttpEndpoint" feature, on a random free port), with the
/// pieces every test needs: the manager to act on the server side, and a URI to connect a raw client to.
/// </summary>
sealed class WebSocketChannelHost : IAsyncDisposable
{
    readonly RunningAspNetServer _server;

    WebSocketChannelHost( RunningAspNetServer server, Uri wsUri )
    {
        _server = server;
        WsUri = wsUri;
        Manager = server.Services.GetRequiredService<WebSocketChannelManager>();
    }

    public Uri WsUri { get; }

    public WebSocketChannelManager Manager { get; }

    /// <summary>
    /// Builds the StObjMap once. Only the manager is needed (with the "HttpEndpoint" feature of the party): features
    /// are simulated by the tests acting directly on it, which is exactly the surface a feature sees.
    /// </summary>
    public static async Task<IStObjMap> BuildMapAsync()
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Path = TestHelper.BinFolder;
        configuration.FirstBinPath.Assemblies.Add( AspNetServerTestHelperExtensions.HttpEndpointAssembly );
        configuration.FirstBinPath.Types.Add( typeof( WebSocketChannelManager ) );
        return (await configuration.RunSuccessfullyAsync()).LoadMap();
    }

    /// <summary>
    /// Starts the party: the WebSocketChannelManager contributes the channel to its endpoint.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="shutdownTimeout">The shutdown timeout of the host and of the web application of its endpoint.</param>
    /// <returns>The running party.</returns>
    public static async Task<WebSocketChannelHost> StartAsync( IStObjMap map, TimeSpan? shutdownTimeout = null )
    {
        var server = await map.CreateRunningAspNetServerAsync(
            configureHost: b =>
            {
                if( shutdownTimeout.HasValue ) b.Services.Configure<HostOptions>( o => o.ShutdownTimeout = shutdownTimeout.Value );
            },
            configureEndpoint: web =>
            {
                if( shutdownTimeout.HasValue ) web.Services.Configure<HostOptions>( o => o.ShutdownTimeout = shutdownTimeout.Value );
            } );
        var wsUri = new Uri( "ws" + server.ServerAddress.Substring( "http".Length ).TrimEnd( '/' )
                             + WebApplicationBuilderExtensions.DefaultPath );
        return new WebSocketChannelHost( server, wsUri );
    }

    /// <summary>
    /// Stops the host of the party (it stops its endpoint).
    /// </summary>
    /// <returns>The awaitable.</returns>
    public Task StopAsync() => _server.Host!.StopAsync();

    /// <summary>
    /// Connects a client and returns it with the connection identifier the server negotiated: this
    /// also guarantees the connect path ran and the server is parked in the read loop.
    /// </summary>
    public async Task<(ClientWebSocket Client, string ConnectionId)> ConnectAsync()
    {
        var client = new ClientWebSocket();
        await client.ConnectAsync( WsUri, CancellationToken.None );
        var negotiation = await ReceiveJsonAsync( client );
        var connectionId = negotiation.RootElement.GetProperty( "connectionId" ).GetString();
        Throw.DebugAssert( connectionId != null );
        return (client, connectionId);
    }

    /// <summary>
    /// Gets the server side of a connection returned by <see cref="ConnectAsync"/>, the object a feature
    /// holds to push to that client. Throws if the manager does not know the identifier.
    /// </summary>
    /// <param name="connectionId">The identifier negotiated by <see cref="ConnectAsync"/>.</param>
    /// <returns>The open connection.</returns>
    public WebSocketChannelConnection GetConnection( string connectionId )
    {
        if( !Manager.TryGetConnection( connectionId, out var connection ) )
        {
            Throw.InvalidOperationException( $"Connection '{connectionId}' is not open." );
        }
        return connection;
    }

    /// <summary>
    /// Completes when the manager has raised <see cref="WebSocketChannelManager.ConnectionClosed"/> for
    /// the connection, or fails after 5 seconds.
    /// </summary>
    /// <param name="connectionId">The connection to wait for.</param>
    /// <returns>The awaitable.</returns>
    public Task WaitForCloseAsync( string connectionId )
    {
        var tcs = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        SequentialEventHandler<ConnectionClosedEvent> handler = null!;
        handler = ( monitor, e ) =>
        {
            if( e.ConnectionId == connectionId )
            {
                Manager.ConnectionClosed.Sync -= handler;
                tcs.TrySetResult();
            }
        };
        Manager.ConnectionClosed.Sync += handler;
        return tcs.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
    }

    /// <summary>
    /// Reads one complete message and parses it. The protocol is end-of-message delimited, so a
    /// message can span several frames.
    /// </summary>
    public static async Task<JsonDocument> ReceiveJsonAsync( ClientWebSocket client, int timeoutSeconds = 5 )
    {
        using var cts = new CancellationTokenSource( TimeSpan.FromSeconds( timeoutSeconds ) );
        var buffer = new byte[4096];
        var text = new StringBuilder();
        WebSocketReceiveResult received;
        do
        {
            received = await client.ReceiveAsync( buffer, cts.Token );
            text.Append( Encoding.UTF8.GetString( buffer, 0, received.Count ) );
        }
        while( !received.EndOfMessage );
        return JsonDocument.Parse( text.ToString() );
    }

    public ValueTask DisposeAsync() => _server.DisposeAsync();
}
