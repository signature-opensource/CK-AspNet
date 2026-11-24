The "InProcess" option of the "HttpEndpoint" AppIdentity feature: the web application of the endpoint runs on an in-memory server instead of Kestrel, without any port.

`"HttpEndpoint": { "InProcess": true }` (or `{ "InProcess": { "Url": "app://localhost" } }`) replaces "Urls" and
"Kestrel": the requests are sent by code to the server (`InProcessHttpEndpointExtensions.GetInProcessServer`, an ASP.NET
`TestServer`), for instance by a desktop window that relays the requests of its page.

Main types: `InProcessHttpEndpointOption`, `InProcessHttpEndpointExtensions`.

See the [README](https://github.com/signature-opensource/CK-AspNet/blob/stable/CK.AppIdentity.HttpEndpoint.InProcess/README.md).
