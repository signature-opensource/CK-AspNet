Helpers that give an ASP.NET Core application a request monitor and an error guard.

`CKBuild` wraps `WebApplicationBuilder.Build`: it registers the request `IActivityMonitor` as a
scoped service, optionally registers a CKomposable map, runs the deferred configuration callbacks
other packages left on the builder, then assembles the pipeline.

A middleware sits at the pipeline head by default. It sets up the scoped HTTP context and catches
what the rest of the pipeline throws, logging it as fatal and closing the request monitor. The
client gets a 500 either way; which path sets it is internal.

Middlewares register as actions on the builder rather than on the application, in three queues.
