# CK.AppHost

The bootstrap of a CKomposable application host. Every party (the "XXX.Host" of an App/Builder/Host solution) starts the
same way, so the bootstrap is in this package rather than copied in each host: the host project is a `.csproj` with its
references, a one-line `Program.cs` and its `appsettings.json`.

It mirrors `CKomposableAppBuilder` (CK.AppBuilder) on the host side: the builder's `Program.cs` is
`return await CKomposableAppBuilder.RunAsync();`, the host's is:

```csharp
await CK.Core.CKomposableAppHost.RunAsync( args );
```

Only the host project references this package.

## What the bootstrap does

`CKomposableAppHost.Build` is the whole bootstrap:

```csharp
public static IHost Build( string[] args,
                           Action<IActivityMonitor,HostApplicationBuilder, IStObjMap>? configure = null,
                           string? contentRoot = null,
                           Assembly? assembly = null )
{
    assembly ??= Assembly.GetEntryAssembly()
                 ?? Throw.InvalidOperationException<Assembly>( "No entry assembly: the assembly that contains the generated code must be specified." );
    contentRoot ??= WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null;
    var builder = Host.CreateApplicationBuilder( new HostApplicationBuilderSettings { Args = args, ContentRootPath = contentRoot } );
    var monitor = builder.GetBuilderMonitor();
    // The secrets of the development: the "#Dev/Secrets.json" of the solution (where the "#Dev/Store" is).
    if( builder.Environment.IsDevelopment() && !LocalDevSolution.SolutionFolder.IsEmptyPath )
    {
        AddSecretsFile( monitor, builder.Configuration, LocalDevSolution.SolutionFolder.AppendPart( "#Dev" ).AppendPart( ConfigurationSecret.DevFileName ) );
    }
    var map = StObjContextRoot.Load( assembly, monitor )
              ?? Throw.InvalidOperationException<IStObjMap>( $"Unable to load the generated code of '{assembly.GetName().Name}': build its Builder." );

    configure?.Invoke( monitor, builder, map );

    // The values burnt by the build win over every other source (they are not part of the deployed configuration).
    // Before the fleet resolution: a burnt value of a template is resolved for the instance.
    ApplyCompiledConfiguration( monitor, builder.Configuration, map );

    // A fleet: the instance is chosen before anything reads the configuration.
    ApplyInstance( monitor, builder.Configuration, assembly );

    // The secrets last: a handle may be a burnt value, or a value of the instance.
    ApplySecrets( monitor, builder.Configuration );

    // Both do nothing when the process doesn't run as a service.
    builder.Services.AddWindowsService();
    builder.Services.AddSystemd();

    builder.UseCKMonitoring();
    builder.AddApplicationIdentityServiceConfiguration();
    // The monitor of the work outside the requests (the requests of the HttpEndpoints have the one of their ScopedHttpContext).
    builder.Services.AddScoped<IActivityMonitor, ActivityMonitor>();
    builder.Services.AddScoped( sp => sp.GetRequiredService<IActivityMonitor>().ParallelLogger );
    builder.Services.AddStObjMap( monitor, map );
    return builder.CKBuild();
}
```

- **Nothing is specific to a party:** what a party does is in its packages and its App project (real objects, their
  `OnHostStartAsync` methods, the features of [CK.AppIdentity](https://github.com/signature-opensource/CK-AppIdentity/blob/stable/CK.AppIdentity/README.md),
  the web servers of [CK.AppIdentity.HttpEndpoint](../CK.AppIdentity.HttpEndpoint/README.md)), and in its configuration.
- **The generated code** is loaded from the entry assembly (the host).
- **`CKomposableAppHost.RunAsync( args, configure? )`** builds the host and runs it until it stops.
- **A fleet runs one of its instances:** when the configuration has `"InstanceCount"`, `"Instances"` or
  `"MaxInstanceCount"` at its root (a fleet App, projected from its builder's `ckonf.json`), the `"Instance"` key
  (`--Instance=Red` on the command line, an index or a name) chooses the instance, and a last configuration source holds
  the values whose placeholders it resolves (`{Index}`, `{Name}`, `{n+Index}`, and `{App.Name}` for the App whose host
  assembly is "App.Host": see `ConfigurationTemplate` in CK.Configuration). An Opened fleet (`"MaxInstanceCount"`) also
  runs a dynamic instance: its index, after the configured ones (`--Instance=3`). A missing or unknown instance in a
  fleet, an `"Instance"` in a host that is not a fleet, fail the start.
- **The values burnt by the build** (`CK.AppIdentity.CompiledConfiguration`: the values restricted to the `Compile`
  level) are added as a source after the others: a configured value that differs, or that a lock covers, is ignored with
  a warning. The values are never logged.
- **The secrets are resolved last** (see the next section).

## The secrets

A secret is never written in a configuration file: its place holds a handle, `{ "$SECRET": "<Name>" }`
(`ConfigurationSecret` of CK.ResourceSpace.Abstractions; the builder projects it, or burns it, as is). The bootstrap
replaces each handle by the value of the configuration key `Secrets:<Name>`, through a last configuration source:

```jsonc
// appsettings.json (projected by the builder)
"Oidc": { "Google": { "ClientId": "...", "ClientSecret": { "$SECRET": "Google:ClientSecret" } } }
```

- **The value comes from any source of the host:** an environment variable (`Secrets__Google__ClientSecret`), a file
  mounted by a container, a vault provider added by the `configure` action.
- **In development** (the host environment is Development and the solution folder is found, like the `#Dev/Store` of
  [CK.AppIdentity](https://github.com/signature-opensource/CK-AppIdentity/blob/stable/CK.AppIdentity/README.md)), the
  `#Dev/Secrets.json` file of the solution is a source: its root is the content of the `Secrets` section, shared by the
  parties of the solution (`#Dev` is ignored by git).

  ```json
  { "Google": { "ClientSecret": "..." } }
  ```
- **A missing secret fails the start**, with all the missing names at once. A value is never logged.

## The tests

A test builds the host without starting it, with its configuration overrides, its content root and its assembly (the
entry assembly of a test is the test runner):

```csharp
var host = CKomposableAppHost.Build( [],
                                     b => b.Configuration.AddInMemoryCollection( overrides ),
                                     TestHelper.SolutionFolder.Combine( "Front/Front.Host" ),
                                     typeof( SomeTypeOfTheHost ).Assembly );
await host.StartAsync();
```

`configure` is called right after the creation of the builder, before the monitoring and the application identity are
configured.

## Windows service and systemd

The host runs as a Windows service or a systemd service when it is started as one, with nothing to configure:
`WindowsServiceLifetimeHostBuilderExtensions.AddWindowsService` (Microsoft.Extensions.Hosting.WindowsServices) and
`SystemdHostBuilderExtensions.AddSystemd` (Microsoft.Extensions.Hosting.Systemd) do nothing otherwise.

A Windows service starts in `C:\Windows\System32`: its content root (the `appsettings.json`, the `Clients`) is the folder
of the executable.

## The host project

The package's `build` files apply to the project that references the package (not to a test project that references the
host):

- **The host is an executable**: the build fails otherwise.
- **The host references its builder**: the build fails otherwise. This reference only orders the builds: the builder runs
  after its own build and writes the generated code in the `$StObjGen` folder of the host (CK.AppBuilder).
  ```xml
  <ProjectReference Include="..\XXX.Builder\XXX.Builder.csproj" ReferenceOutputAssembly="false" Private="false" />
  ```
  The builder of "XXX.Host" is "XXX.Builder". A host whose project name doesn't end with `.Host` names its builder with
  the `CKAppHostBuilder` property (a project name).
- **The generated code is compiled**: it is included just before the compilation (on a clean build, the default glob of
  the source files is evaluated before the builder writes it).
- **The `Clients` folder is not part of the project**: the client applications are built by their own tool chain.
- **The host runs in its project folder** (`dotnet run`, Visual Studio): the package sets `RunWorkingDirectory`, so
  the content root is the project folder (its `appsettings.json`, its `Clients`).
- **The host is a party** of the `#Dev` folder of the development solution: the package sets `CKAppIdentityParty` to
  `true` (see [CK.AppIdentity.Hosting](https://github.com/signature-opensource/CK-AppIdentity/blob/stable/CK.AppIdentity.Hosting/README.md)).

A host project:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CK.AppHost" Version="..." />
    <ProjectReference Include="..\XXX.App\XXX.App.csproj" />
    <ProjectReference Include="..\XXX.Builder\XXX.Builder.csproj" ReferenceOutputAssembly="false" Private="false" />
  </ItemGroup>
</Project>
```

## Requires

- `CK.AppIdentity.Hosting` (and through it `CK.Monitoring.Hosting`): the application identity, the monitoring.
- `CK.StObj.Model`: the generated code.
- `Microsoft.Extensions.Hosting.WindowsServices` and `Microsoft.Extensions.Hosting.Systemd`: the service support.
