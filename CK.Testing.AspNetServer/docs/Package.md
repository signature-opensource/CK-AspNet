Starts a configured ASP.NET Core application on a random port and hands back a running server with a
client wired to it.

A real Kestrel on a real port, not an in-memory test handler - which is what lets a test exercise
cookies, redirects and WebSockets the way a browser would. The helper calls `CKBuild`, so the request
monitor and the error guard are in place, and it throws rather than returning a failure.

The client is deliberately minimal and has two traits worth knowing before writing the first test: it
does not follow redirects, and it must not be used concurrently. Its cookie handling works around a
long-standing path bug in `CookieContainer`.
