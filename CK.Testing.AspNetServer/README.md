# CK.Testing.AspNetServer

Starts a configured `WebApplication` on a random port and hands back a
[`RunningAspNetServer`](RunningAspNetServer.cs) with a client available on it. Not a `TestServer` and not
an in-memory handler: a real server listening on a real port, which is what lets a test exercise
cookies over the wire. The client is not a browser. It speaks only HTTP, follows no redirect, and
manages its own cookies.

## The entry point

```csharp
public static async Task<RunningAspNetServer> CreateRunningAspNetServerAsync(
        this WebApplicationBuilder builder,
        IStObjMap? map = null,
        Action<WebApplication>? configureApplication = null )
```

It calls `CKBuild`, so the request monitor and the error guard of
[CK.AspNet](../CK.AspNet/README.md) are in place. It then adds `http://127.0.0.1:0` to `app.Urls`,
invokes your configurator on the built `WebApplication` and starts it - the bind happens there and
the OS picks a free port - then returns once the address is resolved. It throws on anything going
wrong; there is no result to test.

`RunningAspNetServer` is `IAsyncDisposable`, and disposing it stops the application. It exposes the
`Services` and the `Configuration` of the running app, the `ServerAddress` as
`http://127.0.0.1:<port>`, and a `Client`. Both the class summary and `ServerAddress` give
`http://[::1]:60346` as their example, which the bind above makes impossible.

The class summary states how it is meant to grow: *"It should be extended through extension methods.
If state must be maintained, the best way is to register a dedicated singleton service and use the
`Services` from the extension methods to expose the state."* That also makes the state reachable from
inside the running application, not only from the test.

## Writing a test

Build, create, call, assert, dispose. From
[`DefaultCKMiddlewareTests`](../Tests/CK.AspNet.Tests/DefaultCKMiddlewareTests.cs), trimmed to that
cycle:

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
// ...
using( var scope = runningServer.Services.CreateScope() )
{
    var dependent = scope.ServiceProvider.GetRequiredService<HttpContextDependentService>();
    dependent.HttpContextIsHere.ShouldBeFalse();
}
```

Keep the `await using`. `DisposeAsync` disposes the client and calls `StopAsync` on the application,
so a test that drops it leaves a listening server behind for the rest of the run.

`GetAsync( "" )` is relative to `BaseAddress`, which is the `ServerAddress` the OS just assigned. A
test never writes a port.

Everything registered on the builder has to be registered before the call.
`CreateRunningAspNetServerAsync` calls `CKBuild`, which drains the pipeline queues and freezes the
service collection.

The server can also be inspected from the inside rather than over HTTP.
`runningServer.Services.CreateScope()` gives you the application's own container, which is how the
test ends: a scoped service resolved there finds no `HttpContext`, because no request is in flight.

### It does not cover WebSockets

No WebSocket test in this repository uses this helper, and the two test projects have different
reasons.

`CK.AspNet.WebSocket.Tests` cannot: it references only `CK.AspNet.WebSocket`, so the helper is not on
its compile path. Its two hosts call plain `builder.Build()` and assemble the socket URI as
`"ws://" + Authority + Path`.

`CK.AspNet.WebSocketChannel.Tests` does reference the helper and still starts its own host. The reason
is not the server-side objects - `RunningAspNetServer.Services` is the application's own container and
reaches them - nor the `ws://` address, which `ServerAddress` is enough to build. It is that the
helper keeps the `WebApplication` private and only calls `StopAsync` on disposal, while the shutdown
tests need the app itself: they time `StopAsync` and then dispose it. That host otherwise performs the
same steps as this helper - `CKBuild`, `app.Urls.Add( "http://127.0.0.1:0" )`, `StartAsync`, then read
the address off `IServerAddressesFeature` - and derives the socket URI from it:

```csharp
var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.First();
var wsUri = new Uri( "ws" + address.Substring( "http".Length ).TrimEnd( '/' )
                     + WebApplicationBuilderExtensions.DefaultPath );
```

`DefaultPath` is `"/ws"`, a constant of `CK.AspNet.WebSocketChannel`. A `RunningAspNetServer` gives
you the same two ingredients: `ServerAddress` is that address, and `Services` is the application's
container.

> ⚠️ The `<Description>` of this package advertises two methods that do not exist:
> `WebApplicationBuilder.BuildAndCreateRunningAspNetServerAsync()` and
> `TestHelper.CreateMinimalAspNetServerAsync()`. There is exactly one entry point, the one above, and
> a `<see cref>` in the source points at the second of those ghosts as well.

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

- `CK.AspNet`, whose `CKBuild` this helper calls.

- `CK.Testing.Monitoring`, for the `using static CK.Testing.MonitorTestHelper;` behind the two
  `TestHelper.Monitor` calls that trace the started address and report a failed start. The extension
  itself hangs off `WebApplicationBuilder`, not off a test helper.

- The `Microsoft.AspNetCore.App` framework reference.
