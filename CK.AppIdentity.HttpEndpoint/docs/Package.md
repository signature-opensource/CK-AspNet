The "HttpEndpoint" AppIdentity feature: the web server of a party.

A party is a Generic Host. Its root party's "HttpEndpoint" feature starts the `WebApplication` of its
`Local:HttpEndpoint` configuration once the party is initialized, and stops it before the party stops: one endpoint,
one origin, one pipeline. The application configures the services and the pipeline of the endpoint with
`ConfigureHttpEndpoint` (on the host builder).

The endpoint is configured further by its options (`HttpEndpointOption`), like the forwarded headers behind a reverse
proxy (`ForwardedHeadersOption`).

Main types: `HttpEndpointFeatureDriver` (its `Endpoint`), `RunningHttpEndpoint`, `HttpEndpointHostApplicationBuilderExtensions.ConfigureHttpEndpoint`, `HttpEndpointOption`.

See the [README](https://github.com/signature-opensource/CK-AspNet/blob/stable/CK.AppIdentity.HttpEndpoint/README.md).
