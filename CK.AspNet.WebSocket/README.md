# CK.AspNet.WebSocket

A WebSocket server for ASP.NET Core: a pipe-based duplex transport, message protocols, and one
message dispatcher for each mounted endpoint.

You mount an endpoint with a single `app.Use...` call. The package adds no endpoint routing, no
endpoint metadata, and no authorization services. An application that uses it does not need
`UseRouting()`.

```csharp
builder.Services.AddWebSocketServer();
// ...
app.UseWebSocketServer<MyIn, MyOut>( "/my-ws", b =>
{
    b.UseEndOfMessageDelimitedProtocol( new MyProtocol() );
    b.UseDispatcher<MyDispatcher>();
} );
```

## Mounting the endpoint

[`AddWebSocketServer`](WebSocketServerServiceCollectionExtensions.cs) registers two singletons: the
connection manager and the connection dispatcher. It pulls in no routing or authorization services.

[`UseWebSocketServer`](WebSocketServerApplicationBuilderExtensions.cs) mounts one endpoint. Its
behavior has five points that matter:

- It builds the protocol and the dispatcher **once**, at mount time. One dispatcher serves every
  client of the endpoint, so the dispatcher must be safe for concurrent use.

- It calls `app.UseWebSockets()` on the **main** pipeline, not only on the matched path.

- It matches the path by exact `PathString` equality. The match is case-insensitive, but it has no
  trailing-slash tolerance, so `"/ws/"` is not `"/ws"`. A request on another path flows to the next
  middleware.

- A request on the path that is not a WebSocket upgrade gets a `400` with `"WebSocket request expected."`.

- It sets `options.WebSockets.FramePackets` from the protocol, not from the options you pass. The
  end-of-message-delimited protocol turns it on. A custom protocol leaves it off.

You can call `UseWebSocketServer` more than once. Each call adds its own middleware instance. Extra
instances are inert, but a host normally mounts each endpoint once.

## Protocols and dispatchers

You provide two things: a protocol and a dispatcher. Both are mandatory.
[`MessageDispatcherBuilder`](MessageDispatcherBuilder.cs) validates them at mount time, not at the
first connection. If one is missing, it throws and names the method you did not call.

The protocol is one of two kinds:

- [`IDelimitedMessageProtocol<TIn, TOut>`](Protocol/IDelimitedMessageProtocol.cs), through
  `UseEndOfMessageDelimitedProtocol`. The wrapper lets one message span several WebSocket frames.

- [`IMessageProtocol<TIn, TOut>`](Protocol/IMessageProtocol.cs), through `UseCustomProtocol`. With a
  custom protocol, you control the frames yourself.

The dispatcher is [`IWebSocketMessageDispatcher<in TIn, out TOut>`](IWebSocketMessageDispatcher.cs). It
has four methods: `OnConnectedAsync`, `DispatchMessageAsync`, `OnDisconnectedAsync`, and
`OnParsingIssueAsync`. `OnParsingIssueAsync` runs when the protocol cannot read a message. You register
the dispatcher in one of two ways:

- `UseDispatcher<T>()` builds it with `ActivatorUtilities.CreateInstance` from the **application**
  services. Its constructor can take singleton services only, not scoped ones.

- `UseDispatcher( instance )` takes a dispatcher that you create.

For each connection, the dispatcher receives an
[`IWebSocketConnectionContext<TOut>`](IWebSocketConnectionContext.cs). Its surface is `ConnectionId`,
`User`, `Abort()`, and `WriteAsync`. It carries no monitor.

## Logging and the request monitor

Each dispatcher callback receives an `IActivityMonitor` as its first parameter. The connection context
does not expose one.

The middleware resolves the request monitor with `context.GetRequestMonitor()`, a
`GetRequiredService<IActivityMonitor>()`. A host that registers no scoped monitor does not fail at
startup. Instead, every upgrade on the path throws and returns a `500`. In practice, `CKBuild`
registers the monitor.

A WebSocket connection is one long-lived request, so one monitor can cover its whole life. Here it does
not. `OnConnectedAsync` and `OnDisconnectedAsync` receive the request monitor. `DispatchMessageAsync`
and `OnParsingIssueAsync` receive a different monitor, created for each connection inside the read loop:

```csharp
var monitor = new ActivityMonitor( $"Dispatching messaging loop for '{connection.ConnectionId}'." );
```

So the two callbacks that see every message log to a monitor that is not the request monitor. The
vendored internal code logs through `ActivityMonitor.StaticLogger`.

## Origin and license

The transport, connection, and protocol code comes from [SimpleR](https://github.com/vadrsa/simpler),
at commit `d3377943c3dbfc0afe2b55539d92700a20d44d6b` (the source of the SimpleR.Server 1.0.0 and
SimpleR.Protocol 1.0.0 packages). SimpleR is MIT licensed, Copyright (c) 2024 Davit Asryan. It derives
from the HttpConnections layer of ASP.NET Core SignalR (MIT, .NET Foundation).

The package ships the license as [`LICENSE.SimpleR`](LICENSE.SimpleR). The csproj marks it
`Pack="true"`, so it travels in the `.nupkg`, not only in the repository.

## Changes from upstream

This package keeps the vendored code diffable against SimpleR. Backpressure, write serialization,
graceful and ungraceful close, `CloseTimeout`, and frame packetization stay as upstream wrote them.
Four areas changed:

- **The entry point.** Upstream's `AddSimpleR` and `MapSimpleR` needed `UseRouting`.
  `AddWebSocketServer` and `UseWebSocketServer` do not. They also drop `AddConnections()`, the
  `IAuthorizeData` endpoint metadata, and the copy of dispatcher attributes.

- **Renames and a namespace collapse.** `IWebsocketConnectionContext` becomes
  `IWebSocketConnectionContext`. `WebSocketOptions` becomes `WebSocketTransportOptions`. The four
  `SimpleR.*` namespaces collapse into `CK.AspNet.WebSocket`. The two extension classes keep their
  `Microsoft.*` namespaces, so their methods appear without a `using`.

- **Logging.** The code logs through CK's `IActivityMonitor` instead of `ILogger` and
  `ILoggerFactory`. See the request monitor section above.

- **Three behavior fixes.** Each fix has a `CK deviation from upstream` comment at its site:

  - [`FrameReader.ReadFrame`](Protocol/FrameReader.cs): upstream's `input.Length < length + 1` ignores
    the four-byte length header. A frame received in parts made the next `Slice` throw. The fix returns
    `false` and waits for more bytes.

  - `WebSocketConnectionHandler`: a cancelled read no longer reports abnormal termination as graceful.

  - `WebSocketsServerTransport`: the transport now consumes the sent bytes, so it does not re-send the
    same buffer.

## The `WebSocket` name trap

Inside `CK.AspNet.*`, the simple name `WebSocket` resolves to the `CK.AspNet.WebSocket` namespace, not
to `System.Net.WebSockets.WebSocket`. A `CK.AspNet.*` project that writes `WebSocket` as a type gets
`CS0118: 'WebSocket' is a namespace but is used like a type`.

To correct it, fully qualify the type, or add an alias:

```csharp
using WebSocket = System.Net.WebSockets.WebSocket;
```

Put the alias **after** the file-scoped namespace declaration. The namespace match wins over an alias
above it.

## Requirements

- `CK.AspNet`, for `GetRequestMonitor` and the ASP.NET framework reference that this project does not
  declare itself.

- `CK.ActivityMonitor.SimpleSender`, for the `Debug`, `Trace`, `Warn`, and `Error` log extensions that
  the vendored code uses.
