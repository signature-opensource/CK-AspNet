Helpers that give an ASP.NET Core application a request monitor and an error guard.

`CKBuild` replaces `WebApplicationBuilder.Build`: it registers the request `IActivityMonitor` as a
scoped service, optionally registers a CKomposable map, runs the deferred configuration callbacks
other packages left on the builder, then assembles the pipeline.

A middleware sits at its head. It sets up the scoped HTTP context and catches whatever the rest of
the pipeline throws, logging it as fatal and closing the request monitor - and, when the failure is
asynchronous, answering 500.

Middlewares are registered as actions on the builder rather than on the application, in three queues.
