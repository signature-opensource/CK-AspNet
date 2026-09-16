# CK.AspNet

The CKomposable entry point for ASP.NET Core: four extension methods, one middleware, one scoped
object. Together they give every request a monitor reachable from any scoped service, and turn an
unhandled exception into a logged 500 instead of a lost stack trace.

```csharp
// map is the StObjMap of the application, loaded by StObjContextRoot.Load(...);
// omit it if you only want the request monitor and the error guard.
var app = builder.CKBuild( map );
```

## CKBuild

[`CKBuild`](ApplicationBuilderCKAspNetExtensions.cs) wraps `WebApplicationBuilder.Build()`. Nothing in
the package requires it: the middleware and the scoped object work under a hand-assembled pipeline,
and the last section shows how. It does four things.

1. It registers three scoped services, chained onto one another:

```csharp
builder.Services.AddScoped<ScopedHttpContext>();
builder.Services.AddScoped( sp => sp.GetRequiredService<ScopedHttpContext>().Monitor );
builder.Services.AddScoped( sp => sp.GetRequiredService<IActivityMonitor>().ParallelLogger );
```

`IActivityMonitor` is then resolvable in any request-scoped service, and it is the request monitor,
not a new one.

2. The `map` is optional. Pass one if the application uses CKomposable services, omit it if you only
   want the request monitor and the error guard. When given, `AddStObjMap` registers it, and the
   `SimpleServiceContainer` passed along carries the `WebApplicationBuilder` as a startup service.
   A real object can then take the builder as a parameter of `ConfigureServices` or
   `RegisterStartupServices` - not of `StObjConstruct`, since the map is built before `CKBuild` runs.
   Nothing in the stack uses this yet.

3. `ApplyAutoConfigure()` runs the deferred configuration callbacks other packages registered on the
   builder. A package that registered a callback and never sees `CKBuild` is silently not configured.

4. `Build()` is called, then the middleware pipeline is assembled.

## CKMiddleware

[`CKMiddleware`](CKMiddleware.cs) does two things. Only one of them is about errors.

**It fills the context.**

```csharp
public Task InvokeAsync( HttpContext ctx, ScopedHttpContext scoped )
```

`scoped` is injected. The container builds it, one per request scope, because step 1 registered it.
The middleware never creates one; it fills it. `scoped.Setup( ctx )` runs on every request, successful
or not. It is preceded by a `Throw.DebugAssert( scoped.HttpContext is null )`, so the middleware
expects to run once per scope, and being `[Conditional( "DEBUG" )]` it says nothing in a shipped
build. Without that pass, `ScopedHttpContext.HttpContext` stays null for the life of that scope and a
scoped service built on it has nothing to read.

**Then it guards.** Nothing downstream is allowed to throw silently.

```csharp
else if( t.Status == TaskStatus.Faulted )
{
    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    // ... unwraps a single-exception AggregateException ...
    LogError( ctx.RequestServices, null, ex );
    tcs.SetException( ex );
}
```

Three cases, three behaviours. On success the monitor is closed with `MonitorEnd()`. On a fault the
status becomes 500, the exception is logged at `Fatal`, and the monitor is closed with
`"Request error."`. On cancellation it closes with `"Request canceled."`. Two of the three are not
failures: the request monitor is closed either way.

The guard matters only on the failure path. It puts your logs and the exception in the same place.
Code that injected `IActivityMonitor`, logged its way through a request and then threw would
otherwise leave that monitor stopping at its last written line, with the exception in the host's own
logger.

A middleware throwing synchronously is caught by the `catch` instead, and logged as
*"Synchronous error in next middleware."*. It assigns no `StatusCode`, but it also ends with
`tcs.SetException( ex )`, so the client still gets a 500. The difference is internal.

Logging degrades rather than fails. With no `IActivityMonitor` in the request services, it falls back
to the `IParallelLogger`, then to `ActivityMonitor.StaticLogger`.

## The pipeline queues

Before `CKBuild` there is no `IApplicationBuilder` to call `app.Use...` on. You register an action on
the builder instead, and `CKBuild` replays it at the right place. Unless you know otherwise, that call
is `AppendApplicationBuilder`:

```csharp
builder.AppendApplicationBuilder( app => app.UseMiddleware<StupidMiddleware>() );
```

`PrependApplicationBuilder`'s own summary says *"`AppendApplicationBuilder` should almost always be
used instead of this"*. An appended action is replayed after `CKMiddleware`, so `IActivityMonitor`
resolves to the request monitor and `ScopedHttpContext.HttpContext` is already set. Both can be
parameters of `InvokeAsync`, or injected into a scoped service the middleware uses.
[`DefaultCKMiddlewareTests`](../Tests/CK.AspNet.Tests/DefaultCKMiddlewareTests.cs) is the working
example.

| Method | Runs | Order |
|--------|------|-------|
| `PrependApplicationBuilder( configure, beforeCKMiddleware: true )` | before `CKMiddleware` | **reversed** |
| `PrependApplicationBuilder( configure )` | after `CKMiddleware`, before everything else | **reversed** |
| `AppendApplicationBuilder( configure )` | last | registration order |

The reversal is what "prepend" means. Each new prepend has to end up in front of the previous one, so
the queue is replayed from the last registered to the first. Append keeps its order for the same
reason.

`beforeCKMiddleware: true` is the escape hatch. What you lose is `ScopedHttpContext.HttpContext`,
still null at that point, and the error guard: your middleware runs outside the `try` and the
continuation, so what it throws never reaches `LogError` and never lands in the request monitor.
The client still gets a 500, produced by the host, but nothing in the CK log says why.

You do not lose the monitor. Resolving `IActivityMonitor` there creates the one `CKMiddleware`
will keep, since `Setup` only makes a new one when there is none.

Two things the signatures do not tell you.

**Registering after `CKBuild` is a silent no-op.** The queues are read and removed from the builder
properties there, so a later call lands in a fresh list nobody replays. No exception, no warning. The
`builder` object is still around and still accepts the call, which is what makes the mistake easy to
write.

The recovery depends on which queue you missed. `app.UseXxx( ... )` on the returned `WebApplication`
reproduces an append, and only that. `IApplicationBuilder.Use` appends, and by then `CKMiddleware` and
the three queues above are in place, so your middleware lands last. Neither prepend can be reproduced
that way, least of all `beforeCKMiddleware: true`, since nothing inserts in front of a middleware
already registered. For those, the call has to move above `CKBuild`.

**Your own `IActivityMonitor` registration loses to `CKBuild`'s.** It registers one at step 1, after
whatever you put in `builder.Services`, and resolution takes the last registration. Registering after
`CKBuild` does not help: `Build()` has frozen the collection and it throws *"The service collection
cannot be modified because it is read-only."*

## Registering CKMiddleware by hand

The class is public for an application that assembles its pipeline itself.
`app.UseMiddleware<CKMiddleware>()` is the easy half. The other half is what `CKBuild` did around it.

`ScopedHttpContext` has to be resolvable from the request services, since `InvokeAsync` takes it as an
injected parameter. Miss it and nothing fails at startup. Every request then dies on *"Unable to
resolve service for type 'CK.AspNet.ScopedHttpContext' while attempting to Invoke middleware
'CK.AspNet.CKMiddleware'."*, a 500 produced by the host before the guard runs, so nothing is logged
through the CK chain either.

The other two registrations of step 1 do not gate invocation. They decide whether the exception
reaches the request monitor or the fallback chain, since `LogError` resolves `IActivityMonitor` from
`ctx.RequestServices` and never looks at the `scoped` parameter it was handed.

`ApplyAutoConfigure()` is no longer called either, so every deferred configuration callback another
package registered is silently skipped. No test covers this path.

## ScopedHttpContext

[`ScopedHttpContext`](ScopedHttpContext.cs) is a `[ScopedContainerConfiguredService]` exposing the
`HttpContext` and the request `IActivityMonitor`:

```csharp
public IActivityMonitor Monitor => _monitor ??= new ActivityMonitor();
```

Nothing is built up front. The monitor is created by the first caller that asks for it, which may be
`CKMiddleware`'s `Setup`, a middleware registered ahead of it, or code running outside any request:
a startup task, a test, a scope created by hand. All three get a monitor rather than a null reference.
Within one scope they get the same one, since `ScopedHttpContext` is scoped and holds it in a field.
A hand-made scope is a different instance with a monitor of its own.

`HttpContext` has no such fallback. Only `Setup` assigns it, so outside a request it is null. The
non-nullable declaration does not advertise that nullness, and one test relies on it: a scoped service
built on `HttpContext`, resolved in a hand-made scope, finds nothing.

[`HttpContextCKAspNetExtensions.GetRequestMonitor`](HttpContextCKAspNetExtensions.cs) is the way in
from the other side. Given an `HttpContext`, it hands back the same monitor, for code that has the
context but no scoped service to inject into.

## Requirements

- `CK.StObj.Model`, for `IStObjMap` and `AddStObjMap`.

- `CK.Monitoring.Hosting`, for `ApplyAutoConfigure` and the builder monitor.

- The `Microsoft.AspNetCore.App` framework reference.
