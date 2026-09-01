# CK.WebSocket

WebSocket server infrastructure: a pipe-based duplex transport, message protocols
(including end-of-message frame packetization) and per-connection message dispatchers,
mounted as a **plain middleware**:

```csharp
builder.Services.AddWebSocketServer();
...
app.UseWebSocketServer<MyIn, MyOut>( "/my-ws", b =>
{
    b.UseEndOfMessageDelimitedProtocol( new MyProtocol() );
    b.UseDispatcher<MyDispatcher>();
} );
```

No endpoint routing is involved: an application using this (and, e.g., CK.Cris.AspNet and
CK.AspNet.WebSocketChannel which are also middleware-based) does not need `UseRouting()` at all.

## Origin and license

The transport, connection and protocol code is vendored from
[SimpleR](https://github.com/vadrsa/simpler) at commit
`d3377943c3dbfc0afe2b55539d92700a20d44d6b` (the source of the SimpleR.Server 1.0.0 and
SimpleR.Protocol 1.0.0 packages). SimpleR is MIT licensed, Copyright 2023 Davit Asryan.
SimpleR itself derives from ASP.NET Core SignalR's HttpConnections layer (MIT, .NET Foundation).

## Changes from upstream

- The endpoint-routing entry point (`AddSimpleR` / `MapSimpleR`, which required `UseRouting`)
  is replaced by `AddWebSocketServer` / `UseWebSocketServer` (a path-matching middleware).
  `AddConnections()`, the `IAuthorizeData` endpoint metadata and the dispatcher-attribute
  copying are gone with it.
- `UseWebSockets()` is added to the main pipeline by `UseWebSocketServer` (upstream scoped it
  to the matched endpoint). Path matching is exact `PathString` equality: `"/ws/"` is not `"/ws"`.
  A non-WebSocket request on the path is answered `400`.
- Renamed: `IWebsocketConnectionContext` → `IWebSocketConnectionContext`,
  `WebSocketOptions` → `WebSocketTransportOptions`. Namespaces collapse into `CK.WebSocket`.
- Everything else (backpressure, write serialization, graceful/ungraceful close and
  `CloseTimeout`, frame packetization, abort semantics) is upstream code, kept diffable.
- Bug fix: `FrameReader.ReadFrame`'s boundary check accounts for the 4-byte length header
  (upstream `input.Length < length + 1` made a partially received frame throw instead of
  returning `false`). The deviation is marked by a comment at the change site.
- The namespace collapse also forced a `AspNetTransferFormat` alias in
  `WebSocketsServerTransport.cs`, exactly as in `WebSocketConnectionContext.cs`.

## Warning: the `WebSocket` simple name

Inside any `CK.*` namespace, the simple name `WebSocket` now resolves to the `CK.WebSocket`
namespace, not to `System.Net.WebSockets.WebSocket`. If a `CK.*` project hits
`CS0118: 'WebSocket' is a namespace but is used like a type`, either fully qualify the type or
add `using WebSocket = System.Net.WebSockets.WebSocket;` **after** its file-scoped namespace
declaration (a top-of-file alias does not win over the namespace match).
