using CK.Core;
using NUnit.Framework;
using Shouldly;
using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace CK.WebSocket.Tests;

/// <summary>
/// The connection monitor is the request scoped <see cref="IActivityMonitor"/> of the WebSocket
/// upgrade request (the one CKBuild registers), exactly like CK.Cris.AspNet obtains its monitor.
/// </summary>
[TestFixture]
public class ConnectionMonitorTests
{
    [Test]
    public async Task the_request_scoped_monitor_is_required_Async()
    {
        var (app, baseUri) = await EchoServer.StartAsync( new EchoServer.EchoDispatcher(), requestMonitor: null );
        await using var _ = app;

        using var client = new ClientWebSocket();
        // The middleware fails before the upgrade: the client gets a 500 instead of the 101.
        await Should.ThrowAsync<WebSocketException>( () => client.ConnectAsync( EchoServer.WsUri( baseUri ), CancellationToken.None ) );
    }

    [Test]
    public async Task the_dispatcher_receives_the_request_scoped_monitor_Async()
    {
        var created = new ConcurrentBag<string>();
        var dispatcher = new EchoServer.EchoDispatcher();
        var (app, baseUri) = await EchoServer.StartAsync( dispatcher, () =>
        {
            var m = new ActivityMonitor();
            created.Add( m.UniqueId );
            return m;
        } );
        await using var _ = app;

        using var client = new ClientWebSocket();
        await client.ConnectAsync( EchoServer.WsUri( baseUri ), CancellationToken.None );
        // The echo round trip guarantees OnConnectedAsync ran.
        (await EchoServer.EchoAsync( client, "Hello" )).ShouldBe( "Hello" );

        dispatcher.ConnectionMonitor.ShouldNotBeNull();
        created.ShouldContain( dispatcher.ConnectionMonitor.UniqueId );
    }
    /// <summary>
    /// What one full connection (connect, echo, graceful close) logged: the lines that reached the
    /// monitor itself (through its clients) and the parallel lines (they only reach the static sink).
    /// </summary>
    sealed record LogCapture( IReadOnlyList<ActivityMonitorSimpleCollector.Entry> MonitorLines, IReadOnlyList<string> ParallelLines );

    static async Task<LogCapture> RunOneConnectionAsync()
    {
        var collector = new ActivityMonitorSimpleCollector { MinimalFilter = LogLevelFilter.Debug, Capacity = 1000 };
        var statics = new ConcurrentQueue<(string MonitorId, bool IsParallel, string Text)>();
        ActivityMonitor.StaticLogHandler onStaticLog = ( ref ActivityMonitorLogData d ) => statics.Enqueue( (d.MonitorId, d.IsParallel, d.Text) );
        ActivityMonitor.OnStaticLog += onStaticLog;
        try
        {
            string? monitorId = null;
            var dispatcher = new EchoServer.EchoDispatcher();
            var (app, baseUri) = await EchoServer.StartAsync( dispatcher, () =>
            {
                // Debug level: the finest transport lines (upstream Trace) must be visible.
                var m = new ActivityMonitor { MinimalFilter = LogFilter.Debug };
                m.Output.RegisterClient( collector );
                monitorId = m.UniqueId;
                return m;
            } );
            await using var _ = app;

            using var client = new ClientWebSocket();
            await client.ConnectAsync( EchoServer.WsUri( baseUri ), CancellationToken.None );
            (await EchoServer.EchoAsync( client, "Hello" )).ShouldBe( "Hello" );
            await client.CloseAsync( WebSocketCloseStatus.NormalClosure, "", CancellationToken.None );
            await dispatcher.Disconnected.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            // Stopping the host waits for the connection request to complete: every line has been emitted.
            await app.StopAsync();

            monitorId.ShouldNotBeNull();
            var parallel = statics.Where( s => s.MonitorId == monitorId && s.IsParallel ).Select( s => s.Text ).ToList();
            return new LogCapture( collector.Entries.ToList(), parallel );
        }
        finally
        {
            ActivityMonitor.OnStaticLog -= onStaticLog;
        }
    }

    [Test]
    public async Task transport_logs_are_parallel_lines_of_the_connection_monitor_Async()
    {
        var logs = await RunOneConnectionAsync();
        logs.ParallelLines.ShouldContain( l => l.StartsWith( "Socket opened" ) );
        logs.ParallelLines.ShouldContain( "Socket closed." );
    }
    [Test]
    public async Task connection_manager_logs_are_parallel_lines_of_the_connection_monitor_Async()
    {
        var logs = await RunOneConnectionAsync();
        logs.ParallelLines.ShouldContain( l => l.StartsWith( "New connection " ) );
        logs.ParallelLines.ShouldContain( l => l.StartsWith( "Removing connection " ) );
    }

    [Test]
    public async Task connection_context_logs_are_parallel_lines_of_the_connection_monitor_Async()
    {
        var logs = await RunOneConnectionAsync();
        // Which side (application or transport) completes first is not deterministic: only the
        // dispose line is always there.
        logs.ParallelLines.ShouldContain( l => l.StartsWith( "Disposing connection " ) );
    }

    [Test]
    public async Task handler_logs_are_regular_lines_of_the_connection_monitor_Async()
    {
        var logs = await RunOneConnectionAsync();
        // The handler is the sequential application flow of the connection: it uses the monitor itself.
        logs.MonitorLines.ShouldContain( e => e.Text == "OnConnectedAsync started." );
        logs.MonitorLines.ShouldContain( e => e.Text == "OnConnectedAsync ending." );
    }
}
