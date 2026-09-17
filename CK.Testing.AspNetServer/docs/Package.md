Starts a configured ASP.NET Core application on a random port and hands back a running server with a
client available on it.

A real server on a real port, not an in-memory test handler - which is what lets a test exercise
cookies over the wire. The helper calls `CKBuild`, so the request monitor and the error guard are in
place, and it throws rather than returning a failure.

The client is not a browser and is deliberately minimal: it speaks only HTTP, follows no redirect,
and must not be used concurrently. Its cookie handling carries a partial fix for a long-standing
`CookieContainer` path bug. The WebSocket tests build their own host.
