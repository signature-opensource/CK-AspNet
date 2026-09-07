# CK.WebSocket

WebSocket server infrastructure: a pipe-based duplex transport, message protocols
(including end-of-message frame packetization) and per-connection message dispatchers,
mounted as a plain middleware - no endpoint routing (`UseRouting`) is required.

Logging goes through CK's `IActivityMonitor`: each connection uses the request scoped monitor
of its upgrade request (registered by `CKBuild`), which must be available. Dispatchers reach it
through `IWebSocketConnectionContext.Monitor`.

The transport, connection and protocol code is vendored and adapted from
[SimpleR](https://github.com/vadrsa/simpler) (MIT, Copyright (c) 2024 Davit Asryan).
The full license text ships in this package as `LICENSE.SimpleR`; the repository
README documents the changes from upstream.
