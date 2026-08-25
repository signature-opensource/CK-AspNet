using System;

namespace CK.AspNet.WebSocketChannel;

/// <summary>
/// Argument of <see cref="WebSocketChannelManager.ConnectionClosed"/>.
/// <para>
/// It carries the identifier rather than the connection because the connection is already out of the
/// manager and disposed when this is raised: what a feature has to do here is drop whatever it keyed
/// by <see cref="ConnectionId"/>, not talk to the socket.
/// </para>
/// </summary>
/// <param name="ConnectionId">The identifier of the connection that has been closed.</param>
/// <param name="Exception">The exception that caused the disconnection, null on a normal close.</param>
public readonly record struct ConnectionClosedEvent( string ConnectionId, Exception? Exception );
