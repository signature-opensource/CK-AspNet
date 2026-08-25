using System;

namespace CK.AspNet.WebSocketChannel;

/// <summary>
/// Argument of <see cref="WebSocketChannelManager.MessageReceived"/>.
/// <para>
/// <see cref="Message"/> is a copy: handlers may await freely and keep it, unlike the sequence the
/// protocol hands over (see <see cref="RawMessageProtocol.ParseMessage"/>).
/// </para>
/// <para>
/// Nothing here is authenticated. The socket is anonymous by construction and no validator ever saw
/// this message: a feature must treat it as it would treat a query string, and route anything
/// privileged through the Cris endpoint instead.
/// </para>
/// </summary>
/// <param name="Connection">The connection the message came from.</param>
/// <param name="Topic">The topic the client sent it under. A feature ignores what is not its own.</param>
/// <param name="Message">The payload, as the JSON value the client put in the envelope.</param>
public readonly record struct MessageReceivedEvent( WebSocketChannelConnection Connection,
                                                   string Topic,
                                                   ReadOnlyMemory<byte> Message );
