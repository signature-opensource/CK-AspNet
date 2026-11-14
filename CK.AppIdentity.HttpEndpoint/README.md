# CK.AppIdentity.HttpEndpoint

A party of an AppIdentity application is a Generic Host. Being a web server is one of its features: the
"HttpEndpoint" feature of the root party starts its `WebApplication` once the party is initialized, and stops it
before the party stops. A local party has one endpoint: one origin (its public URL), one pipeline. The endpoint is
configured with the party, in its `Local:HttpEndpoint` section, not by the ASP.NET defaults (`"Urls"`, `"Kestrel"`,
`ASPNETCORE_URLS`).

It builds on [CK.AspNet](../CK.AspNet/README.md) (`CKBuild` builds the web application) and on
[CK.AppIdentity](https://github.com/signature-opensource/CK-AppIdentity/blob/stable/CK.AppIdentity/README.md):
`HttpEndpointFeatureDriver` is an `ApplicationIdentityStaticFeatureDriver`.

## Configuration

The feature is opt-in: the root party allows it (`Local:AllowFeatures`). `Local:HttpEndpoint` has either `Urls`
(separated by `;`, or an array) or a `Kestrel` section (Kestrel's own configuration: `Endpoints`, `Certificates`,
`Limits`...): several listening addresses don't need several pipelines. With a `Kestrel` section, the optional `DefaultEndpoint`
names the Kestrel endpoint whose URL is the default one (see `RunningHttpEndpoint.DefaultUrl` below).

```json
"CK-AppIdentity": {
  "FullName": "Sample/$Front/#Dev",
  "Local": {
    "AllowFeatures": "HttpEndpoint",
    "HttpEndpoint": { "Urls": "http://localhost:5300" }
  }
}
```

These are errors that fail the initialization of the party (nothing listens):
- no `Local:HttpEndpoint` section, or one with both or none of `Urls` and `Kestrel` (a child section that has `Urls` or
  `Kestrel` is reported as a named endpoint: there is no endpoint level, its content goes in `Local:HttpEndpoint`);
- a `DefaultEndpoint` with `Urls` (the first URL is the default one), or one that names no `Kestrel:Endpoints:<Name>:Url`;
- a key of `Local:HttpEndpoint` that is neither `Urls`, `Kestrel`, `DefaultEndpoint` nor one of its [options](#the-options-of-the-endpoint),
  and an error in the configuration of an option;
- a tenant domain that allows the feature: only the root party has an endpoint;
- in Development, the ASP.NET defaults: the `urls`, `http_ports`, `https_ports` keys and the `Kestrel` section at the
  root of the configuration, and the `ASPNETCORE_URLS`, `ASPNETCORE_HTTP_PORTS`, `ASPNETCORE_HTTPS_PORTS`
  environment variables. Otherwise they are a warning: they are ignored.

A port in use fails the start of the host.

## The services and the pipeline of the endpoint

The packages do the job: the web application of the endpoint gets the contributions of the packages of the
BinPath, registered by their real objects in the host's `WebApplicationContributions` (see
[the "Enable" convention](../CK.AspNet/README.md#the-enable-convention-and-the-contributions-of-the-packages)). The
host's code doesn't depend on the packages:

```csharp
var builder = Host.CreateApplicationBuilder( args );
builder.UseCKMonitoring();
builder.AddApplicationIdentityServiceConfiguration();
builder.Services.AddStObjMap( monitor, map );
var host = builder.CKBuild();
```

What the endpoint serves is decided by the packages of the BinPath, where it serves it by `Local:HttpEndpoint`.

The application can add its own configuration to the endpoint with
`HttpEndpointHostApplicationBuilderExtensions.ConfigureHttpEndpoint`, in one gesture on its `WebApplicationBuilder`,
after the contributions of the packages. It can be called more than once: the configurations apply in their order.

```csharp
builder.ConfigureHttpEndpoint( web => web.AppendApplicationBuilder( app => app.UseMiddleware<MyMiddleware>() ) );
```

The services of the endpoint are its own (its server, its routing, its lifetime...) and the host's:

| Service of the host | In the web application |
|---|---|
| A singleton | Forwarded to the host: the same instance |
| A singleton instance, an open generic (`IOptions<>`...) | Copied |
| A scoped or transient service | Copied: created in the scopes of the requests |
| A container-configured service (`[SingletonContainerConfiguredService]`, `[ScopedContainerConfiguredService]`), even a singleton | Copied: each web application has its own instance |
| `ILoggerFactory` | The host's one: one logging pipeline |
| `IHostedService`, `IActivityMonitor`, `IParallelLogger` | Not copied: the web application has its own (the request monitor of `CKBuild`) |

The web application doesn't handle the console nor the process lifetime: the host does.

## The options of the endpoint

The endpoint is configured further by its options: what a package adds to its web application when the party enables
it. An option is a [party component option](https://github.com/signature-opensource/CK-AppIdentity/blob/stable/CK.AppIdentity/PartyComponentOptions/README.md)
of the `HttpEndpointFeatureDriver`: a class that specializes [`HttpEndpointOption`](HttpEndpointOption.cs), decorated
with `[FeatureOption]`. Its key in `Local:HttpEndpoint` is its name (its type name without the "Option" suffix): `true`
or a section enables it, `false` disables it, and its default applies when the key is absent.

- `HttpEndpointOption.Initialize` reads and checks its configuration when the feature is set up: an error fails the initialization of the party.
- `HttpEndpointOption.ConfigureServices` configures the services of the web application, before the contributions of
  the packages.
- `HttpEndpointOption.ConfigurePipeline` adds its middlewares: the options' middlewares are the first ones of the
  pipeline, before the CKMiddleware, in the order of the options (their topology attributes order them).

The options are known by the engine (CK.AppIdentity.Engine), not discovered at runtime: a BinPath with this feature
needs the `CK.AppIdentity.Engine` package.

### The forwarded headers

Behind a reverse proxy, the endpoint sees the proxy: its scheme (often `http` when the proxy terminates TLS), its host
and its address. [`ForwardedHeadersOption`](ForwardedHeadersOption.cs) applies the `X-Forwarded-For`,
`X-Forwarded-Proto` and `X-Forwarded-Host` headers sent by the trusted proxies, so that the whole pipeline sees the
client: the logs, and what is derived from the request's origin (the callback URL of a remote login, the `Secure` flag
of the cookies, the comparison with the `Origin` of a browser).

```jsonc
"HttpEndpoint": {
  "Urls": "http://0.0.0.0:5300",
  "ForwardedHeaders": {
    "KnownProxies": "10.0.0.12",     // IP addresses, separated by ';' (or an array).
    "KnownNetworks": "10.0.0.0/8",   // CIDR networks, separated by ';' (or an array).
    "Headers": "XForwardedFor, XForwardedProto, XForwardedHost", // The default.
    "ForwardLimit": 1                // The default.
  }
}
```

It fails closed: the proxies are trusted explicitly, never by default (not even the loopback). `"ForwardedHeaders": true`,
a section without `KnownProxies` nor `KnownNetworks`, an invalid address or network and an unknown key are errors.

## Lifetime

- **Start:** after the initialization of the party (all its features are set up), outside the agent's loop
  (`ApplicationIdentityStaticFeatureDriver.OnHostStartedAsync`). `HttpEndpointFeatureDriver.Endpoint` is then the
  `RunningHttpEndpoint` (its `WebApplication`, its actual `Urls`: a port 0 is resolved); it is null when not running.
  - **`RunningHttpEndpoint.DefaultUrl` is the URL to show** (a browser, a status).
    - **With `Urls`,** it is the actual URL of the first configured one: the order of `Urls` expresses the preference
      (put https first if it is the one to use).
    - **With a `Kestrel` section,** whose endpoints the configuration sorts by name, it is the URL of the endpoint named
      by `DefaultEndpoint`, otherwise the first https URL, otherwise the first one.
  - **A wildcard host** (`*`, `+`, `0.0.0.0`, `[::]`) is replaced by `localhost`.
  - **It is the bound URL:** behind a reverse proxy or in a container, the public URL differs.
- **Stop:** before the party stops (`ApplicationIdentityStaticFeatureDriver.OnHostStoppingAsync`): the requests in
  progress complete while the party is still whole. The host's `IHostApplicationLifetime.ApplicationStopping` is raised
  first, as a web application raises it before its server drains: what aborts its long running requests on
  `ApplicationStopping` (the WebSocket channel of CK.AspNet.WebSocketChannel) does it before the drain. (The host itself
  raises it after the `IHostedLifecycleService.StoppingAsync` of its services, where the static features stop.) A web application is stopped, never disposed: its container
  would dispose the singletons it forwards to the host.
- **A party lives and dies with its endpoint:** a web application that stops by itself stops the host.

## The public URL of the party

The `HttpEndpointFeatureDriver` gives the public URL of the root party (its `IParty.Description`, see the
"PartyDescription" feature of
[CK.AppIdentity](https://github.com/signature-opensource/CK-AppIdentity/blob/stable/CK.AppIdentity/README.md#the-description-of-a-party))
when `Local:PublicUrl` is not configured: the `DefaultUrl` of the started endpoint (a port 0 is resolved). Behind a
reverse proxy or a public domain name, the party configures its `Local:PublicUrl`.

## The running status of a development party

In a development solution, the "DevRunningStatus" feature of
[CK.AppIdentity](https://github.com/signature-opensource/CK-AppIdentity) writes the running status of the party
(`#Dev/Store/.../DevRunningStatus.json`, read by the `#Dev/Start.ps1` launcher). The `HttpEndpointFeatureDriver` adds
the `Http` section: the
`Urls` and the `DefaultUrl` of the running endpoint (null when it is not running). The party is `Ready` once the endpoint
is started.
