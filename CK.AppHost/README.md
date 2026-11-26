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
    // The generated code is loaded first: a host built for a deployment has the environment of its party (its burnt
    // full name), that is the environment of the host when nothing else gives one. The monitor is the builder's one
    // (its logs are replayed once the monitoring is configured).
    var monitor = new ActivityMonitor( ActivityMonitorOptions.WithInitialReplay, nameof( IHostApplicationBuilder ) );
    var map = StObjContextRoot.Load( assembly, monitor )
              ?? Throw.InvalidOperationException<IStObjMap>( $"Unable to load the generated code of '{assembly.GetName().Name}': build its Builder." );
    var settings = new HostApplicationBuilderSettings { Args = args, ContentRootPath = contentRoot };
    var burntEnvironment = GetBurntEnvironmentName( map );
    if( burntEnvironment != null && !HasEnvironmentName( args ) )
    {
        settings.EnvironmentName = burntEnvironment;
        monitor.Info( $"The environment of the host is the one of its party: '{burntEnvironment}'." );
    }
    var builder = Host.CreateApplicationBuilder( settings );
    ((IHostApplicationBuilder)builder).Properties[typeof( IActivityMonitor )] = monitor;
    // The secrets of the development: the "#Dev/Secrets.json" of the solution (where the "#Dev/Store" is).
    if( builder.Environment.IsDevelopment() && !LocalDevSolution.SolutionFolder.IsEmptyPath )
    {
        AddSecretsFile( monitor, builder.Configuration, LocalDevSolution.SolutionFolder.AppendPart( "#Dev" ).AppendPart( ConfigurationSecret.DevFileName ) );
    }
    // The secrets given as files: the credentials of a systemd service, a mounted secret volume. Before the configure
    // action: a vault it adds wins.
    AddSecretsDirectories( monitor, builder.Configuration, Environment.GetEnvironmentVariable );

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
- **The environment of the party is the reference:** a host built for a deployment (CK.AppDeployment) has the full name
  of its party burnt in its generated code (`Sample/Auth3/$Front/#Production`): its environment (`Production`) is the
  .NET environment of the host when neither the command line (`--environment`) nor the environment variables
  (`DOTNET_ENVIRONMENT`, `ASPNETCORE_ENVIRONMENT`) give one. A development build burns nothing. CK.AppIdentity checks
  them at start: a `#Dev` party in a host that is not in Development is a warning, another party in a host in
  Development is an error.
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
"Oidc": { "Schemes": { "Google": { "ClientId": "...", "ClientSecret": { "$SECRET": "Google:ClientSecret" } } } }
```

- **The value comes from any source of the host:** an environment variable (`Secrets__Google__ClientSecret`), a file of
  a folder of secrets (below), a vault provider added by the `configure` action (it wins over the files, which win over
  the environment variables).
- **A folder of secrets:** each file is a secret named after it, `__` separating the segments of the name (as in an
  environment variable: the file `Google__ClientSecret` is the secret `Google:ClientSecret`); its value is its content
  without one final line break. Two folders are read, the second wins:
  - `$CREDENTIALS_DIRECTORY` (`CKomposableAppHost.CredentialsDirectoryVariable`): systemd sets it for a service that
    declares credentials. A credential encrypted by `systemd-creds` is bound to the machine (its TPM, or its host key) and
    decrypted by systemd at start, into a folder of the service only:

    ```
    systemd-creds encrypt --with-key=tpm2 --name=KeyRing key.txt /etc/credstore.encrypted/KeyRing
    # In the unit of the service:
    LoadCredentialEncrypted=KeyRing
    ```
  - `$CK_SECRETS_DIRECTORY` (`CKomposableAppHost.SecretsDirectoryVariable`): any folder, typically a mounted secret volume
    of a container (its `..data` folder and the files whose name starts with a `.` are ignored, so are the sub folders).

  A folder that doesn't exist, a file whose name is not a secret name, an empty value fail the start. The names read are
  logged, never the values.
- **In development** (the host environment is Development and the solution folder is found, like the `#Dev/Store` of
  [CK.AppIdentity](https://github.com/signature-opensource/CK-AppIdentity/blob/stable/CK.AppIdentity/README.md)), the
  `#Dev/Secrets.json` file of the solution is a source: its root is the content of the `Secrets` section, shared by the
  parties of the solution (`#Dev` is ignored by git).

  ```json
  { "Google": { "ClientSecret": "..." } }
  ```
- **A missing secret fails the start**, with all the missing names at once. A value is never logged. Like any failure of
  `RunAsync` (an invalid configuration, a port already in use), it is written on the standard error and logged, and the
  process exits with the code 1: never an unhandled exception (an abort, a core dump on Linux).
- **An optional handle**, `{ "$SECRET?": "<Name>" }`, names a secret that may be missing: its path is then absent (as if
  nothing were configured there) and the missing optional names are logged. A one-time enlistment ticket is given this
  way (`{ "$SECRET?": "Enlist:Worker2" }`: `Secrets__Enlist__Worker2` at the first start only), and so is a feature
  activated by its secret.

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

**The content root** (the `appsettings.json`, the `Clients`) is the folder of the executable for a Windows service (it
starts in `C:\Windows\System32`) and for any published host (`dotnet publish` writes a `CK.AppHost.Published.json` file
next to it): it can be started from any directory (a systemd unit needs no `WorkingDirectory`). The command line
(`--contentRoot`) or the environment (`DOTNET_CONTENTROOT`, `ASPNETCORE_CONTENTROOT`) still chooses another one. A host run
from its project (`dotnet run`, Visual Studio) has its project folder as its content root.

## The host project

The package's `build` files apply to the project that references the package (not to a test project that references the
host):

- **The host is an executable**: the build fails otherwise.
- **The host references the Builder of its AppContext**: the build fails otherwise. This reference only orders the
  builds: the Builder runs after its own build and writes the generated code in the `$StObjGen` folder of the host
  (CK.AppBuilder).
  ```xml
  <ProjectReference Include="..\Front.Builder\Front.Builder.csproj" ReferenceOutputAssembly="false" Private="false" />
  ```
  The Builder is named after its AppContext (`{AppContext}.Builder`), not after the host: any referenced `*.Builder`
  project is it, unless the `CKAppHostBuilder` property names it (a project name).
- **The generated code is compiled**: it is included just before the compilation (on a clean build, the default glob of
  the source files is evaluated before the builder writes it).
- **The `Clients` folder is not part of the project**: the client applications are built by their own tool chain.
- **The host runs in its project folder** (`dotnet run`, Visual Studio): the package sets `RunWorkingDirectory`, so
  the content root is the project folder (its `appsettings.json`, its `Clients`).
- **The `appsettings.json` is published with the host**, also by a host of the base SDK (`Microsoft.NET.Sdk`: a worker;
  the Web SDK publishes it), with `CK.AppHost.Published.json`: the content root of a published host is its folder. This
  file holds the effective properties of the publication (`"Publish"`: the `Executable` to start, `RuntimeIdentifier`,
  `SelfContained`, `PublishSingleFile`...) for the platform that receives the host; CK.AppDeployment adds the version and the deployment
  (`"Deployment"`: its manifest). A published host logs its version and its deployment at start.
- **A published host has no XML documentation**: `PublishDocumentationFiles` and `PublishReferencesDocumentationFiles`
  default to false.
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
