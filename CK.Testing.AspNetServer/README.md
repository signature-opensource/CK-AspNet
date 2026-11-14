# CK.Testing.AspNetServer

Starts a party, or a standalone web application, on a random port and hands back a
[`RunningAspNetServer`](RunningAspNetServer.cs) with a client available on it. Not a `TestServer` and not
an in-memory handler: a real server listening on a real port, which is what lets a test exercise
cookies over the wire. The client is not a browser. It speaks only HTTP, follows no redirect, and
manages its own cookies.

## A party, from its map

```csharp
public static async Task<RunningAspNetServer> CreateRunningAspNetServerAsync(
        this IStObjMap map,
        Action<MutableConfigurationSection>? identity = null,
        Action<HostApplicationBuilder>? configureHost = null,
        Action<WebApplicationBuilder>? configureEndpoint = null )
```

It starts what runs in production (see CK.AppHost): a Generic Host whose web application is the
endpoint of its "HttpEndpoint" feature
([CK.AppIdentity.HttpEndpoint](../CK.AppIdentity.HttpEndpoint/README.md)). The endpoint gets the
contributions of the packages (`WebApplicationContributions`: Cris, the client applications, the WebSocket
channel...), the services of the host, then `configureEndpoint`; the public URL of the party is the
endpoint's one.

- **The map contains the "HttpEndpoint" feature:** add `HttpEndpointAssembly` ("CK.AppIdentity.HttpEndpoint") to the
  BinPath. Without it, the helper throws.
- **The identity** is "Test/$Server" by default, the `identity` action changes it. Whatever it does, the
  "HttpEndpoint" feature is allowed and the endpoint listens on `http://127.0.0.1:0` unless it is configured
  (`Local:HttpEndpoint:Urls` or `Local:HttpEndpoint:Kestrel`): the OS picks a free port.
- **The host** has no default configuration source, the "Production" environment and the test project folder as its
  content root. `configureHost` is called before the map is registered.
- **The routes of a test** go to the endpoint, in one gesture on its builder:

```csharp
await using var server = await map.CreateRunningAspNetServerAsync(
    configureEndpoint: web => web.AppendApplicationBuilder( app => ((WebApplication)app).MapGet( "/hello", () => "hello" ) ) );
```

`Services` and `Configuration` are the host's: the singletons of the endpoint are forwarded to them.
`Host` is the host, `Application` the web application of the endpoint. Disposing the server stops and
disposes the host.

## A standalone web application

```csharp
public static async Task<RunningAspNetServer> CreateRunningAspNetServerAsync(
        this WebApplicationBuilder builder,
        Action<WebApplication>? configureApplication = null )
```

For a test of a middleware or of the pipeline of [CK.AspNet](../CK.AspNet/README.md): no map, no party.
It calls `CKBuild`, so the request monitor and the error guard are in place, adds `http://127.0.0.1:0`
to `app.Urls`, invokes `configureApplication` and starts the application. `Host` is null; disposing
the server stops the application.

Everything registered on the builder has to be registered before the call: `CKBuild` drains the
pipeline queues and freezes the service collection. From
[`DefaultCKMiddlewareTests`](../Tests/CK.AspNet.Tests/DefaultCKMiddlewareTests.cs):

```csharp
var builder = WebApplication.CreateSlimBuilder();
builder.Services.AddScoped<HttpContextDependentService>();
// ...
builder.AppendApplicationBuilder( app => app.Run( context =>
{
    context.Response.StatusCode = (int)HttpStatusCode.Unused;
    return Task.CompletedTask;
} ) );

await using var runningServer = await builder.CreateRunningAspNetServerAsync();

using( HttpResponseMessage test = await runningServer.Client.GetAsync( "" ) )
{
    test.StatusCode.ShouldBe( HttpStatusCode.Unused );
}
```

## Common to both

Both throw on anything going wrong: there is no result to test. Keep the `await using`: a test that
drops the server leaves a listening server behind for the rest of the run. `GetAsync( "" )` is
relative to `BaseAddress`, the `ServerAddress` the OS just assigned: a test never writes a port.

`RunningAspNetServer` is sealed: it is extended through extension methods. State is best kept in a
dedicated singleton service, reached from `Services`, which also makes it reachable from inside the
running application.

The WebSocket channel tests ([`WebSocketChannelHost`](../Tests/CK.AspNet.WebSocketChannel.Tests/WebSocketChannelHost.cs))
start a party: the socket URI is `"ws" + ServerAddress[4..] + "/ws"`, and they time the stop of its `Host`.

## The client

[`RunningClient`](RunningAspNetServer.RunningClient.cs) has fifteen public request methods:

- `GetAsync`, `PostAsync` (form values), `PostJsonAsync`, `PostXmlAsync` - each with a `string` and
  a `Uri` overload.

- `GetStringAsync`, `GetByteArrayAsync`, `GetStreamAsync` - likewise.

- `PostAsync( Uri, HttpContent )`, which has no `string` twin.

All accept a url relative to `BaseAddress`, or an absolute one.

**It does not follow redirects.** A 3xx comes back as a 3xx. For a test of an authentication flow that
is the feature: you get to assert on the `Location`.

**It must not be used concurrently**, and nothing enforces it. There is no lock and no `Interlocked`
anywhere in the package, and its one assertion guards the resolved server address, not the client.

The handler reads the cookie container before the send and writes the response's cookies back into it
after, over one container shared by every request of the client. Overlap two requests and the second
reads that container before the first has stored its `Set-Cookie`. That second request goes out with
whatever the container held at that instant - the stale cookie, or no `Cookie` header at all if it
was still empty - and nothing throws. What the server answers is your application's business, but the
request that produced it was not the one you wrote.

`Token` is settable, which is how a test switches identity without rebuilding anything. The bearer
only goes out when the url is under `BaseAddress`: the handler tests
`Token != null && _baseAddress.IsBaseOf( requestUri )`, so an absolute url pointing elsewhere carries
no `Authorization` header however the token is set. On the path that does add it, `Token` is read
twice - once to test, once to concatenate. Clear it in between and `"Bearer " + null` is what reaches
`Headers.Add`. And `RunningAspNetServer.Client` is itself an ungated
`_client ??= new RunningClient( this )`, so two threads reaching for it first end up with two
clients and two cookie containers.

`CookieContainer` is not settable: it is a get-only property over a readonly field, so you can empty
or fill the container but not swap it. The class summary says both can be changed; only one can.

## Cookie handling

The client's `DelegatingHandler` carries a partial fix for the `CookieContainer` path behaviour
described in [dotnet/corefx#21250](https://github.com/dotnet/corefx/issues/21250#issuecomment-309613552).
Do not rely on path handling for a cookie carrying more than one `path=`: the fix indexes the original
header while removing from an already-shortened copy, so the second removal takes the wrong span or
throws. Nothing tests it, and no test in the repository exercises cookies at all.

Path is all the handler corrects. Everything else - expiry, domain, `Secure` - is whatever
`CookieContainer.SetCookies` and `GetCookieHeader` do on their own. Good enough for tests of your own
application, not for a browser.

[`CookieContainerExtensions.ClearCookies`](CookieContainerExtensions.cs) clears a base path and
optional sub paths, which is the "log out and start over" of an authentication test.

## Requirements

- [CK.AppIdentity.HttpEndpoint](../CK.AppIdentity.HttpEndpoint/README.md) for the party (it brings
  [CK.AspNet](../CK.AspNet/README.md), whose `CKBuild` the standalone web application calls).

- `CK.Testing`, for `TestHelper.Monitor` and `TestHelper.TestProjectFolder`.

- `CK.AppIdentity.Engine`: a BinPath with the "HttpEndpoint" feature has the options of the endpoint (the
  `PartyComponentOptionDirectory` of CK.AppIdentity), that this engine generates.

- The `Microsoft.AspNetCore.App` framework reference.
