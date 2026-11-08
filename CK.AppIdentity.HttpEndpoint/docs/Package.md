The "HttpEndpoint" AppIdentity feature: the web servers of a party.

A party is a Generic Host. Its root party's "HttpEndpoint" feature starts one `WebApplication` per endpoint of its
`Local:HttpEndpoint` configuration once the party is initialized, and stops them before the party stops. The
application configures the services and the pipeline of an endpoint with `AddHttpEndpoint` (on the host builder).

An endpoint is configured further by its options (`HttpEndpointOption`), like the forwarded headers of an endpoint behind a
reverse proxy (`ForwardedHeadersOption`).

Main types: `HttpEndpointFeatureDriver` (its `Endpoints`), `RunningHttpEndpoint`, `HttpEndpointHostApplicationBuilderExtensions.AddHttpEndpoint`, `HttpEndpointOption`.

See the [README](https://github.com/signature-opensource/CK-AspNet/blob/stable/CK.AppIdentity.HttpEndpoint/README.md).
