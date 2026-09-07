using CK.Core;
using CK.PerfectEvent;
using NUnit.Framework;
using Shouldly;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace CK.AspNet.WebSocketChannel.Tests;

/// <summary>
/// The monitor a feature receives in the channel events is the connection monitor of CK.WebSocket:
/// the request scoped monitor of the socket, the one its transport logs to.
/// </summary>
[TestFixture]
public class ConnectionMonitorTests
{
    [Test]
    public async Task channel_events_are_raised_with_the_connection_monitor_of_the_transport_Async()
    {
        var map = await WebSocketChannelHost.BuildMapAsync();
        await using var host = await WebSocketChannelHost.StartAsync( map );

        // Parallel lines (the transport's) only reach the static sink: capture them there.
        var statics = new ConcurrentQueue<(string MonitorId, string Text)>();
        ActivityMonitor.StaticLogHandler onStaticLog = ( ref ActivityMonitorLogData d ) => statics.Enqueue( (d.MonitorId, d.Text) );
        string? eventMonitorId = null;
        SequentialEventHandler<WebSocketChannelConnection> onOpened = ( monitor, c ) => eventMonitorId = monitor.UniqueId;
        ActivityMonitor.OnStaticLog += onStaticLog;
        host.Manager.ConnectionOpened.Sync += onOpened;
        try
        {
            var (client, _) = await host.ConnectAsync();
            using( client )
            {
                eventMonitorId.ShouldNotBeNull();
                // "Socket opened" is logged by the transport right after the upgrade, before the negotiation
                // message the client just received could be sent.
                statics.Where( s => s.MonitorId == eventMonitorId ).Select( s => s.Text )
                       .ShouldContain( t => t.StartsWith( "Socket opened" ),
                                       "The channel must raise its events with the monitor the transport logs to." );
            }
        }
        finally
        {
            host.Manager.ConnectionOpened.Sync -= onOpened;
            ActivityMonitor.OnStaticLog -= onStaticLog;
        }
    }
}
