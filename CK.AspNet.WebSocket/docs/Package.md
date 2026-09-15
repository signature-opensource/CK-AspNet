WebSocket server infrastructure: pipe-based duplex transport, message
protocols with optional end-of-message framing, one dispatcher per mounted endpoint.

Mounting is a plain middleware call - no endpoint routing, no `UseRouting`, no authorization services
pulled in. The path match is exact, and a non-WebSocket request on it is answered 400.

A connection is one long-lived request, so a dispatcher gets the request scoped `IActivityMonitor` as
the first parameter of every method. That monitor is required.

Transport and protocol code is vendored from SimpleR (MIT, Copyright (c) 2024 Davit Asryan);
`LICENSE.SimpleR` ships in the package and the README lists the deviations.
