WebSocket server infrastructure: pipe-based duplex transport, message
protocols with optional end-of-message framing, one dispatcher per mounted endpoint.

Mounting is a plain middleware call - no endpoint routing, no `UseRouting`, no authorization services
pulled in. The path match is whole and case-insensitive; a non-WebSocket request on it gets a 400.

Every dispatcher method takes an `IActivityMonitor` first: the required request scoped one on connect
and disconnect, a per-connection one in the message loop.

Transport and protocol code is vendored from SimpleR (MIT, Copyright (c) 2024 Davit Asryan);
`LICENSE.SimpleR` ships in the package; the deviations are listed in the repository.
