# CK.AspNet.WebSocketChannel

One WebSocket shared by every feature of an application: one anonymous socket per client, one process
singleton that owns them all, and a `{topic, message}` envelope that tells each feature which frames
are its own. Features do not open sockets and do not mount endpoints. They observe, and they push.

```csharp
builder.AddWebSocketChannel();
var app = builder.CKBuild( map );
app.UseWebSocketChannel();          // defaults to "/ws"
```

## Setting it up

The sequence above comes from
[`WebSocketChannelHost`](../Tests/CK.AspNet.WebSocketChannel.Tests/WebSocketChannelHost.cs).

[`AddWebSocketChannel`](WebApplicationBuilderExtensions.cs) does one thing:
`builder.Services.AddWebSocketServer()`. It does not register the manager, which is an `IRealObject`
and arrives only with the StObj map. That is why `CKBuild( map )` is on the path.

`CK.AspNet` is a transitive dependency. `CK.AspNet.WebSocket` references it, and that edge carries
both the ASP.NET framework reference and the `GetRequestMonitor` the server calls on every upgrade.

`UseWebSocketChannel` mounts the endpoint as a plain middleware: no endpoint routing, no `UseRouting`.
Call it once. Every feature shares this endpoint and is routed by topic, so there is nothing to mount
per feature. The path defaults to `DefaultPath`, the constant `"/ws"`, and the client must use the
same one. Underneath it is `UseWebSocketServer<ReadOnlySequence<byte>, ReadOnlyMemory<byte>>`,
configured with [`RawMessageProtocol`](RawMessageProtocol.cs) and
[`WebSocketChannelDispatcher`](WebSocketChannelDispatcher.cs). Those two are public but not for you.

A feature touches two things. [`WebSocketChannelManager`](WebSocketChannelManager.cs) is an
`IRealObject` it injects, the same instance for the whole process. The
[`WebSocketChannelConnection`](WebSocketChannelConnection.cs) objects are what the manager hands out.

## The wire format

[`WebSocketChannelEnvelope`](WebSocketChannelEnvelope.cs) is the only place that knows it, on both
sides. Every message under a topic is:

```json
{ "topic": "CK.Ng.SomeFeature", "message": <the payload, verbatim> }
```

The payload is embedded with `WriteRawValue( ..., skipInputValidation: true )`. A feature hands over
the exact bytes it would have written on a socket of its own, and pays no re-encoding.

The connection is told its identifier before any feature is notified of it, in a frame that carries
no topic:

```json
{ "connectionId": "..." }
```

The client needs that identifier to tell the server who it is later. It sends it back on the
application's authenticated Cris endpoint, never on this socket.

## Pushing

```csharp
connection.WriteAsync( topic, message );            // this client
manager.SendBroadcastAsync( topic, message );       // every open connection
```

Both build the envelope for you. When the same message goes to several connections but not all, build
the frame once with `WebSocketChannelEnvelope.Create( topic, message )` and write it with the raw
`WriteAsync( frame )` overload. Anything else handed to that overload is not routed by the client.

A push onto a disposed connection is not an error. The call does nothing, because a push racing with
a disconnection is normal. The flag is read before the write lock is taken and again after, and the
lock itself is never disposed: a write racing disposal must stay a silent no-op, and the semaphore
owns nothing that needs releasing.

Broadcast has its own rules. A connection opening during the broadcast may or may not receive it. One
that closed is skipped silently. Per-connection order is preserved, so two broadcasts awaited one
after the other reach every client in that order. An exception from one write propagates.

## Events and ordering

```csharp
public PerfectEvent<WebSocketChannelConnection> ConnectionOpened    => ...;  // manager
public PerfectEvent<ConnectionClosedEvent>      ConnectionClosed    => ...;  // manager
public PerfectEvent<MessageReceivedEvent>       AllMessagesReceived => ...;  // manager
public PerfectEvent<MessageReceivedEvent>       MessageReceived     => ...;  // connection
```

A feature that works per connection subscribes on the connection. It sees that client's traffic only,
and its handlers go away with the connection, so there is nothing to unsubscribe on close. A feature
that wants every connection subscribes on the manager.

The order is by handler kind, not by event. The manager-wide event is fed by a relay bridge built on
the connection's event, and a raise goes kind by kind across that bridge: the source's `.Sync`
handlers, then each bridge's `.Sync`, then the source's `.Async`, then each bridge's `.Async`. So it
is `MessageReceived.Sync`, then `AllMessagesReceived.Sync`, then `MessageReceived.Async`, then
`AllMessagesReceived.Async`. A feature subscribing `.Async` on the connection runs after one that
subscribed `.Sync` on the manager. The comments on both events say the connection's handlers run
first, which holds only between handlers of the same kind.

Guard every handler. `PerfectEvent` calls these raises *safe*, and that covers less than it sounds.
The outer raise swallows and logs, so the socket survives: one faulty feature must not tear down a
socket the others share. But a throwing `.Sync` handler aborts the rest of the raise: the remaining
`.Sync` handlers and every `.Async` handler are skipped for that message, and a throw on the
connection skips `AllMessagesReceived` entirely, so one broken feature silences it for all the
others. A throwing `.Async` handler aborts less, since the `.Sync` handlers have already run, but it
still skips every later `.Async` handler, the manager-wide ones included. Preferring `.Async` narrows
the damage; it does not remove it. `.ParallelAsync` handlers sit outside all this: they are started
before the first `.Sync` handler and only awaited at the end, so a throw elsewhere leaves them
running unobserved.

The connection's declaration says the manager-wide event is still raised, and the manager's says the
same of the connection's event. Neither holds, and both understate what is skipped.

`ConnectionClosed` carries the identifier and not the connection. By then the connection is out of the
manager and disposed, so what a handler can do is drop what it keyed by that identifier. It also
carries an `Exception`, which the declaration documents as null on a normal close.

Topics have a sharp edge, and the code names it:

> Topics live in one flat namespace shared by every feature, so a topic must be globally unique: name
> it after your package. Anything shorter eventually collides, and a collision means another
> feature's payloads reaching your handler - silently, since nothing here can tell the two apart.

The manager also exposes `Count` and `TryGetConnection`, which is how a feature resolves an identifier
it kept.

## Incoming messages

A connection is anonymous by construction. Nothing in this package knows about users, tenants or
domains, and the event argument states the consequence:

> Nothing here is authenticated. The socket is anonymous by construction and no validator ever saw
> this message: a feature must treat it as it would treat a query string, and route anything
> privileged through the Cris endpoint instead.

The channel is a descending pipe in practice. What a client has to say travels on an authenticated
endpoint; what it needs to hear comes back here.

The inbound path is also lazy, so a feature that never reads pays nothing. As long as nobody
subscribes, on a connection or on the manager, incoming frames are never decoded. `ParseMessage`
returns its input unchanged, and the manager returns early when neither side has a handler. Subscribing
turns the cost on, and brings the caveat above with it.

A frame that is not a `{topic, message}` envelope with a string topic does not kill the socket. The
manager catches, logs a warning and drops it: throwing there would tear down a socket the other
features are using.

`MessageReceivedEvent.Message` is a copy. The sequence the protocol hands over borrows the pipe's
buffers and is valid only until the read loop advances, so the payload is copied out of the JSON
document synchronously, before any handler can await. That is what lets a handler keep the message.

## Shutdown

The manager registers `AbortAll` on `ApplicationStopping`. Without it the host would wait out its
shutdown timeout on every open socket. Aborting still starts a graceful close, which waits
`CloseTimeout` (5s) for the client handshake, so `UseWebSocketChannel` sets
`options.WebSockets.CloseTimeout = TimeSpan.Zero`: the socket is torn down at once and shutdown never
depends on client behaviour.

A `_stopping` flag closes the next hole. Once stopping, a newly arriving connection is refused and
aborted, so a reconnecting client cannot re-arm the drain. The flag is re-checked after the connection
was added, since `AbortAll` sets it before iterating and may have missed that entry.

`OnHostStopAsync` is the late backstop, aborting any straggler rather than only disposing it.
`OnHostStart` resets `_stopping`, so the same real object serves a second host on the same StObjMap.

## Requirements

- `CK.AspNet.WebSocket`, the WebSocket server this is built on: `AddWebSocketServer`,
  `UseWebSocketServer`, `IWebSocketConnectionContext`, and the two interfaces the protocol and the
  dispatcher implement. It is a project of this repository, referenced as such.

- `CK.PerfectEvent`, for the four events and the relay that forwards a connection's messages to the
  manager-wide one. It references `CK.ActivityMonitor`, and so does `CK.AspNet.WebSocket` through
  `CK.ActivityMonitor.SimpleSender` - which is where the `monitor.Warn` calls come from.
  `IActivityMonitor` arrives by either edge, and `Throw` from the `CK.Core` underneath.

- `CK.Abstractions`, for `IRealObject`.
