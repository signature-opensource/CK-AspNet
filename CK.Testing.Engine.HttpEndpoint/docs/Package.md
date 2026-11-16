Starts a party (a Generic Host with its "HttpEndpoint" feature) from its StObjMap, or a standalone ASP.NET Core
application, on a random port and hands back a running server with a client available on it.

A real server on a real port, not an in-memory test handler - which is what lets a test exercise
cookies over the wire. The party is what runs in production: the contributions of the packages, the
services of the host forwarded to its endpoint. The helpers throw rather than returning a failure.

The client is not a browser and is deliberately minimal: it speaks only HTTP, follows no redirect,
and must not be used concurrently. Its cookie handling carries a partial fix for a long-standing
`CookieContainer` path bug.
