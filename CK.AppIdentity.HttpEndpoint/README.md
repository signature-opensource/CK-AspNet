# CK.AppIdentity.HttpEndpoint

A party of an AppIdentity application is a Generic Host. Being a web server is one of its features: the
"HttpEndpoint" feature of the root party starts one `WebApplication` per endpoint once the party is initialized,
and stops them before the party stops. The endpoints are configured with the party, in its
`Local:HttpEndpoint` section, not by the ASP.NET defaults (`"Urls"`, `"Kestrel"`, `ASPNETCORE_URLS`).

It builds on [CK.AspNet](../CK.AspNet/README.md) (`CKBuild` builds each web application) and on
[CK.AppIdentity](https://github.com/signature-opensource/CK-AppIdentity/blob/stable/CK.AppIdentity/README.md):
`HttpEndpointFeatureDriver` is an `ApplicationIdentityStaticFeatureDriver`.

## Configuration

The feature is opt-in: the root party allows it (`Local:AllowFeatures`). Each child of `Local:HttpEndpoint` is an
endpoint, named after its key, with either `Urls` (separated by `;`, or an array) or a `Kestrel` section (Kestrel's
own configuration: `Endpoints`, `Certificates`, `Limits`...).

```json
"CK-AppIdentity": {
  "FullName": "Sample/$Front/#Dev",
  "Local": {
    "AllowFeatures": "HttpEndpoint",
    "HttpEndpoint": {
      "Main": { "Urls": "http://localhost:5300" }
    }
  }
}
```

These are errors that fail the initialization of the party (nothing listens):
- no endpoint in `Local:HttpEndpoint`, or an endpoint with both or none of `Urls` and `Kestrel`;
- an endpoint configured by the application (below) that `Local:HttpEndpoint` doesn't define;
- a tenant domain that allows the feature: only the root party has endpoints;
- in Development, the ASP.NET defaults: the `urls`, `http_ports`, `https_ports` keys and the `Kestrel` section at the
  root of the configuration, and the `ASPNETCORE_URLS`, `ASPNETCORE_HTTP_PORTS`, `ASPNETCORE_HTTPS_PORTS`
  environment variables. Otherwise they are a warning: they are ignored.

A port in use fails the start of the host.

## The services and the pipeline of an endpoint

`HttpEndpointHostApplicationBuilderExtensions.AddHttpEndpoint` configures the web application of an endpoint in one
gesture, on its `WebApplicationBuilder`: its services, and its middlewares with
`ApplicationBuilderCKAspNetExtensions.AppendApplicationBuilder` (or `PrependApplicationBuilder`), that `CKBuild`
applies.

```csharp
var builder = Host.CreateApplicationBuilder( args );
builder.AddApplicationIdentityServiceConfiguration();
builder.AddHttpEndpoint( "Main", web =>
{
    web.Services.AddCrisAuthentication();
    web.AppendApplicationBuilder( app =>
    {
        app.UseCrisAuthentication();
        app.UseCris();
        ((WebApplication)app).MapClients();
    } );
} );
builder.Services.AddStObjMap( monitor, map );
var host = builder.CKBuild();
```

The services of an endpoint are its own (its server, its routing, its lifetime...) and the host's:

| Service of the host | In the web application |
|---|---|
| A singleton | Forwarded to the host: the same instance |
| A singleton instance, an open generic (`IOptions<>`...) | Copied |
| A scoped or transient service | Copied: created in the scopes of the requests |
| A container-configured service (`[SingletonContainerConfiguredService]`, `[ScopedContainerConfiguredService]`), even a singleton | Copied: each web application has its own instance |
| `ILoggerFactory` | The host's one: one logging pipeline |
| `IHostedService`, `IActivityMonitor`, `IParallelLogger` | Not copied: the web application has its own (the request monitor of `CKBuild`) |

The web application doesn't handle the console nor the process lifetime: the host does.

## Lifetime

- **Start:** after the initialization of the party (all its features are set up), outside the agent's loop
  (`ApplicationIdentityStaticFeatureDriver.OnHostStartedAsync`). `HttpEndpointFeatureDriver.Endpoints` then lists the
  `RunningHttpEndpoint` (its name, its `WebApplication`, its actual addresses: a port 0 is resolved).
- **Stop:** before the party stops (`ApplicationIdentityStaticFeatureDriver.OnHostStoppingAsync`): the requests in
  progress complete while the party is still whole. A web application is stopped, never disposed: its container
  would dispose the singletons it forwards to the host.
- **A party lives and dies with its endpoints:** a web application that stops by itself stops the host.
