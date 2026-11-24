# CK.AppIdentity.HttpEndpoint.InProcess

A desktop application shows its client in a window of its own process: an HTTP port on the machine would expose its
API to every other process and to the web pages of the browsers. This option of the
[HttpEndpoint feature](../CK.AppIdentity.HttpEndpoint/README.md) runs the web application of the endpoint on an
in-memory server (ASP.NET's `TestServer`): no port is opened, the requests are sent by code, through the same pipeline.

## Configuration

```jsonc
"Local": {
  // The URL is "app://localhost".
  "HttpEndpoint": { "InProcess": true }
  // or: "HttpEndpoint": { "InProcess": { "Url": "app://localhost" } }  (an absolute URL without path)
}
```

[`InProcessHttpEndpointOption`](InProcessHttpEndpointOption.cs) provides the server of the endpoint
(`HttpEndpointOption.ServerUrl`): the endpoint has no "Urls" nor "Kestrel" (having them is an error). Its URL is the
`RunningHttpEndpoint.DefaultUrl` and the `RunningHttpEndpoint.Urls` of the endpoint, and the scheme and host of its
requests. It is not an http(s) URL: the endpoint contributes no public URL to the description of the party
("Local:PublicUrl" sets one if needed).

The value is typically one of a deployment: the development keeps "Urls" (a browser, a dev server), the installed
deployment of a desktop application is in process.

## Sending requests

[`InProcessHttpEndpointExtensions.GetInProcessServer`](InProcessHttpEndpointExtensions.cs) returns the `TestServer` of a
running endpoint (null when it runs on Kestrel):

```csharp
var endpoint = services.GetRequiredService<HttpEndpointFeatureDriver>().Endpoint;
var server = endpoint?.GetInProcessServer();
HttpContext context = await server.SendAsync( c =>
{
    c.Request.Method = "GET";
    c.Request.Path = "/index.html";
} );
```
