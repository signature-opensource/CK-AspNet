# CK-AspNet

[![Licence](https://img.shields.io/github/license/signature-opensource/CK-AspNet.svg)](LICENSE)

What an application built on the CK stack needs from ASP.NET Core: a request monitor, an error guard,
a WebSocket server, one shared WebSocket channel, the web servers of an AppIdentity party, the bootstrap
of its host, and a way to start the whole thing in a test.

| Package | Description | Latest stable |
|---------|-------------|---------------|
| [CK.AspNet](CK.AspNet/README.md) | `CKBuild`, the request monitor as a scoped service, and the middleware that turns an unhandled exception into a logged 500. | [![nuget](https://img.shields.io/nuget/v/CK.AspNet.svg?label=CK.AspNet)](https://www.nuget.org/packages/CK.AspNet/) |
| [CK.AspNet.WebSocket](CK.AspNet.WebSocket/README.md) | WebSocket server infrastructure - transport, message protocols and a message dispatcher per mounted endpoint - as a plain middleware. | [![nuget](https://img.shields.io/nuget/v/CK.AspNet.WebSocket.svg?label=CK.AspNet.WebSocket)](https://www.nuget.org/packages/CK.AspNet.WebSocket/) |
| [CK.AspNet.WebSocketChannel](CK.AspNet.WebSocketChannel/README.md) | One anonymous socket per client, shared by every feature and routed by topic. | [![nuget](https://img.shields.io/nuget/v/CK.AspNet.WebSocketChannel.svg?label=CK.AspNet.WebSocketChannel)](https://www.nuget.org/packages/CK.AspNet.WebSocketChannel/) |
| [CK.AppIdentity.HttpEndpoint](CK.AppIdentity.HttpEndpoint/README.md) | The "HttpEndpoint" AppIdentity feature: a party is a Generic Host whose web servers (one per endpoint of its `Local:HttpEndpoint` configuration) start and stop with it. | [![nuget](https://img.shields.io/nuget/v/CK.AppIdentity.HttpEndpoint.svg?label=CK.AppIdentity.HttpEndpoint)](https://www.nuget.org/packages/CK.AppIdentity.HttpEndpoint/) |
| [CK.AppHost](CK.AppHost/README.md) | The bootstrap of a CKomposable application host, the same for every party (`CKomposableAppHost.RunAsync`), its Windows service and systemd support, and the MSBuild conventions of the host project. | [![nuget](https://img.shields.io/nuget/v/CK.AppHost.svg?label=CK.AppHost)](https://www.nuget.org/packages/CK.AppHost/) |
| [CK.Testing.AspNetServer](CK.Testing.AspNetServer/README.md) | Starts the application on a random port and hands back a running server with a client. | [![nuget](https://img.shields.io/nuget/v/CK.Testing.AspNetServer.svg?label=CK.Testing.AspNetServer)](https://www.nuget.org/packages/CK.Testing.AspNetServer/) |

They form a chain: `CK.AspNet.WebSocketChannel` → `CK.AspNet.WebSocket` → `CK.AspNet`. Only the
middle link is named in the channel's csproj; `CK.AspNet` arrives through it.
`CK.Testing.AspNetServer` and `CK.AppIdentity.HttpEndpoint` also build on `CK.AspNet`, by calling its
`CKBuild`. `CK.AspNet` and `CK.AppHost` depend on none of the others (`CK.AppHost` doesn't use
ASP.NET: a host's web servers come from `CK.AppIdentity.HttpEndpoint`, through its App project).

Read `CK.AspNet` first. `CKBuild` is what an application calls instead of `Build`, and it puts the
request monitor in place - which `CK.AspNet.WebSocket` then expects to find.
