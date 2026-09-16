# CK-AspNet

[![Licence](https://img.shields.io/github/license/signature-opensource/CK-AspNet.svg)](LICENSE)

What an application built on the CK stack needs from ASP.NET Core: a request monitor, an error guard,
a WebSocket server, one shared WebSocket channel, and a way to start the whole thing in a test.

| Package | Description | Latest stable |
|---------|-------------|---------------|
| [CK.AspNet](CK.AspNet/README.md) | `CKBuild`, the request monitor as a scoped service, and the middleware that turns an unhandled exception into a logged 500. | [![nuget](https://img.shields.io/nuget/v/CK.AspNet.svg?label=CK.AspNet)](https://www.nuget.org/packages/CK.AspNet/) |
| [CK.AspNet.WebSocket](CK.AspNet.WebSocket/README.md) | WebSocket server infrastructure - transport, message protocols and a message dispatcher per mounted endpoint - as a plain middleware. | [![nuget](https://img.shields.io/nuget/v/CK.AspNet.WebSocket.svg?label=CK.AspNet.WebSocket)](https://www.nuget.org/packages/CK.AspNet.WebSocket/) |
| [CK.AspNet.WebSocketChannel](CK.AspNet.WebSocketChannel/README.md) | One anonymous socket per client, shared by every feature and routed by topic. | [![nuget](https://img.shields.io/nuget/v/CK.AspNet.WebSocketChannel.svg?label=CK.AspNet.WebSocketChannel)](https://www.nuget.org/packages/CK.AspNet.WebSocketChannel/) |
| [CK.Testing.AspNetServer](CK.Testing.AspNetServer/README.md) | Starts the application on a random port and hands back a running server with a client. | [![nuget](https://img.shields.io/nuget/v/CK.Testing.AspNetServer.svg?label=CK.Testing.AspNetServer)](https://www.nuget.org/packages/CK.Testing.AspNetServer/) |

They form a chain: `CK.AspNet.WebSocketChannel` → `CK.AspNet.WebSocket` → `CK.AspNet`. Only the
middle link is named in the channel's csproj; `CK.AspNet` arrives through it.
`CK.Testing.AspNetServer` also builds on `CK.AspNet`, by calling its `CKBuild`. `CK.AspNet` is the
only one of the four that depends on none of the others.

Read `CK.AspNet` first. `CKBuild` is what an application calls instead of `Build`, and it puts the
request monitor in place - which `CK.AspNet.WebSocket` then expects to find.
