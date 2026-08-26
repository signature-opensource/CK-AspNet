using CK.Core;
using CK.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
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
/// A running host exposing the channel on a random free port, with the pieces every test needs:
/// the manager to act on the server side, and a URI to connect a raw client to.
/// </summary>
sealed class WebSocketChannelHost : IAsyncDisposable
{
    WebSocketChannelHost( WebApplication app, Uri wsUri )
    {
        App = app;
        WsUri = wsUri;
        Manager = app.Services.GetRequiredService<WebSocketChannelManager>();
    }

    public WebApplication App { get; }

    public Uri WsUri { get; }

    public WebSocketChannelManager Manager { get; }

    /// <summary>
    /// Builds the StObjMap once. Only the manager is needed: features are simulated by the tests
    /// acting directly on it, which is exactly the surface a feature sees.
    /// </summary>
    public static async Task<IStObjMap> BuildMapAsync()
    {
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Path = TestHelper.BinFolder;
        configuration.FirstBinPath.Types.Add( typeof( WebSocketChannelManager ) );
        return (await configuration.RunSuccessfullyAsync()).LoadMap();
    }

    public static async Task<WebSocketChannelHost> StartAsync( IStObjMap map, TimeSpan? shutdownTimeout = null )
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.AddApplicationIdentityServiceConfiguration();
        builder.AddWebSocketChannel();
        if( shutdownTimeout.HasValue )
        {
            builder.Services.Configure<HostOptions>( o => o.ShutdownTimeout = shutdownTimeout.Value );
        }

        var app = builder.CKBuild( map );
        app.Urls.Add( "http://127.0.0.1:0" ); // Random free port.
        app.UseRouting();
        app.UseWebSocketChannel();
        await app.StartAsync();

        var server = app.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var wsUri = new Uri( "ws" + address.Substring( "http".Length ).TrimEnd( '/' )
                             + WebApplicationBuilderExtensions.DefaultPath );
        return new WebSocketChannelHost( app, wsUri );
    }

    /// <summary>
    /// Connects a client and returns it with the connection identifier the server negotiated: this
    /// also guarantees the connect path ran and the server is parked in the SimpleR read loop.
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

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
    }
}
