using CK.Core;
using CK.Monitoring;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Shouldly;

using static CK.Testing.MonitorTestHelper;

namespace CK.AspNet.WebSocketChannel.Tests;

/// <summary>
/// Regression tests for the host-shutdown WebSocket drain bug: an open SimpleR read loop is never
/// aborted on <c>ApplicationStopping</c>, so Kestrel drains it for the whole
/// <see cref="HostOptions.ShutdownTimeout"/> and <see cref="IHost.StopAsync"/> blocks for that long.
/// <para>
/// These tests used to live in CK-Observable-Domain, where they covered this logic by ricochet. The
/// logic now belongs to <see cref="WebSocketChannelManager"/>, so they cover it directly - and for
/// every feature sharing the channel at once.
/// </para>
/// </summary>
[TestFixture]
public class HostShutdownDrainTests
{
    // Short enough to keep the test fast, long enough that the drain bug (5s) is unambiguously slower than a prompt stop.
    static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds( 5 );
    // A correct stop is near-instant; below ShutdownTimeout with headroom so the bug fails clearly.
    static readonly TimeSpan PromptStop = TimeSpan.FromSeconds( 2 );

    [Test]
    public async Task host_stops_promptly_when_no_client_is_connected_Async()
    {
        // Control case: nothing to drain, so the host must stop immediately. Isolates the open connection as the cause.
        var map = await WebSocketChannelHost.BuildMapAsync();
        var host = await WebSocketChannelHost.StartAsync( map, ShutdownTimeout );

        var sw = Stopwatch.StartNew();
        await host.App.StopAsync();
        sw.Stop();
        await host.App.DisposeAsync();

        TestHelper.Monitor.Info( $"Stop with no client connected took {sw.ElapsedMilliseconds} ms." );
        sw.Elapsed.ShouldBeLessThan( PromptStop );
    }

    [Test]
    public async Task host_stops_promptly_when_a_websocket_client_is_connected_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        var host = await WebSocketChannelHost.StartAsync( map, ShutdownTimeout );

        var (client, _) = await host.ConnectAsync();
        using( client )
        {
            // Stop while connected and collect logs: the bug both hangs for ShutdownTimeout and crashes with an OperationCanceledException.
            long elapsedMs;
            using( var collector = GrandOutput.Default!.CreateMemoryCollector( 256 ) )
            {
                var sw = Stopwatch.StartNew();
                await host.App.StopAsync();
                elapsedMs = sw.ElapsedMilliseconds;
                await host.App.DisposeAsync();

                await collector.UpdateCachedEntriesAsync();
                var canceled = collector.CachedEntries
                                        .Where( e => e.Exception?.ExceptionTypeName.Contains( "OperationCanceledException" ) == true )
                                        .ToList();
                canceled.ShouldBeEmpty( "No OperationCanceledException must escape the shutdown path." );
            }

            TestHelper.Monitor.Info( $"Stop with one connected client took {elapsedMs} ms." );
            TimeSpan.FromMilliseconds( elapsedMs ).ShouldBeLessThan( PromptStop,
                "A connected WebSocket client must not delay host shutdown. If this fails near ShutdownTimeout, "
                + "the manager is not aborting the SimpleR connection on ApplicationStopping." );
        }
    }

    [Test]
    public async Task host_stops_promptly_while_a_client_keeps_reconnecting_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        var host = await WebSocketChannelHost.StartAsync( map, ShutdownTimeout );

        var (client, _) = await host.ConnectAsync();
        using( client )
        {
            // Spam reconnects so an attempt lands in the post-ApplicationStopping window (Kestrel still listening).
            // The manager must refuse these; if any were accepted it would re-arm the full ShutdownTimeout drain.
            using var loopStop = new CancellationTokenSource();
            var reconnectLoop = ReconnectLoopAsync( host.WsUri, loopStop.Token );

            var sw = Stopwatch.StartNew();
            await host.App.StopAsync();
            sw.Stop();

            await loopStop.CancelAsync();
            await reconnectLoop;
            await host.App.DisposeAsync();

            TestHelper.Monitor.Info( $"Stop with a reconnecting client took {sw.ElapsedMilliseconds} ms." );
            sw.Elapsed.ShouldBeLessThan( PromptStop,
                "Connections accepted after ApplicationStopping must not re-arm the drain." );
        }
    }

    static async Task ReconnectLoopAsync( Uri wsUri, CancellationToken stop )
    {
        while( !stop.IsCancellationRequested )
        {
            try
            {
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync( wsUri, stop );
                await ws.ReceiveAsync( new byte[1024], stop );
            }
            catch when( !stop.IsCancellationRequested )
            {
                // Connect/receive fails as the host stops: keep trying until cancelled.
            }
            catch( OperationCanceledException )
            {
                break;
            }
        }
    }
}
