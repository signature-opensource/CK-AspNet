using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace CK.Core;

/// <summary>
/// The bootstrap of a CKomposable application host: the same for every party. It mirrors the
/// <c>CK.Setup.CKomposableAppBuilder</c> (CK.AppBuilder) on the host side.
/// <para>
/// This helper should be used with the following conventions:
/// <list type="number">
///     <item>The application code is in a project named "XXX.App".</item>
///     <item>Its builder is an executable project named "XXX.Builder" that writes the generated code in the "XXX.Host".</item>
///     <item>
///     The application is the executable project "XXX.Host" that references this package and whose Program.cs is:
///     <code>
///     await CK.Core.CKomposableAppHost.RunAsync( args );
///     </code>
///     </item>
/// </list>
/// </para>
/// <para>
/// The process can run as a Windows service or a systemd service: nothing to configure (when it doesn't run as a service,
/// the service support does nothing).
/// </para>
/// </summary>
public static class CKomposableAppHost
{
    /// <summary>
    /// Builds the host of the entry assembly and runs it until it stops.
    /// See <see cref="Build(string[], Action{IActivityMonitor, HostApplicationBuilder, IStObjMap}?, string?, Assembly?)"/>.
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    /// <param name="configure">Optional configuration of the builder, called right the creation of the builder and the successful load of the map.</param>
    /// <returns>The awaitable.</returns>
    public static async Task RunAsync( string[] args, Action<IActivityMonitor, HostApplicationBuilder, IStObjMap>? configure = null )
    {
        using var host = Build( args, configure );
        await host.RunAsync().ConfigureAwait( false );
    }

    /// <summary>
    /// Builds the host, not started: <see cref="RunAsync(string[], Action{IActivityMonitor, HostApplicationBuilder, IStObjMap}?)"/> uses it, and the tests
    /// use it to start and stop the host themselves.
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    /// <param name="configure">
    /// Optional configuration of the builder or the <see cref="IStObjMap"/>, called right after the creation of the builder and the
    /// successful load of the map (before the monitoring and the application identity are configured). Tests use this to override the configuration,
    /// real hosts can use it to handle <see cref="IHostApplicationBuilder.Configuration"/> the way they want (typically configuring properties
    /// of the map's real objects).
    /// </param>
    /// <param name="contentRoot">
    /// Optional content root (where the appsettings.json and the Clients are). Defaults to the current directory, or to the
    /// folder of the executable when the process runs as a Windows service (it starts in "C:\Windows\System32").
    /// </param>
    /// <param name="assembly">
    /// Optional assembly that contains the generated code. Defaults to the entry assembly: a test (whose entry assembly is
    /// the test runner) passes the host's assembly.
    /// </param>
    /// <returns>The host, not started.</returns>
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

    /// <summary>
    /// The .NET environment of a host built for a deployment: the environment of the full name of its party, burnt by its
    /// build ("Sample/$Front/#Production" is "Production"). Null for a development build (nothing is burnt).
    /// </summary>
    internal static string? GetBurntEnvironmentName( IStObjMap map )
    {
        if( map.StObjs.Obtain( typeof( CK.AppIdentity.CompiledConfiguration ) ) is not CK.AppIdentity.CompiledConfiguration compiled
            || !compiled.Values.TryGetValue( "CK-AppIdentity:FullName", out var fullName )
            || fullName == null )
        {
            return null;
        }
        var environment = fullName.Split( '/' ).LastOrDefault( p => p.StartsWith( '#' ) );
        return environment == null || environment == CoreApplicationIdentity.DefaultEnvironmentName ? null : environment[1..];
    }

    /// <summary>
    /// Whether the environment of the host is given by its process: the command line ("--environment") or the environment
    /// variables of .NET ("DOTNET_ENVIRONMENT", "ASPNETCORE_ENVIRONMENT"). It wins over the burnt one.
    /// </summary>
    internal static bool HasEnvironmentName( string[] args )
    {
        return !string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "DOTNET_ENVIRONMENT" ) )
               || !string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "ASPNETCORE_ENVIRONMENT" ) )
               || args.Any( a => a.TrimStart( '-', '/' ).StartsWith( HostDefaults.EnvironmentKey, StringComparison.OrdinalIgnoreCase ) );
    }

    /// <summary>
    /// The values that the build burnt in the generated code (<see cref="CK.AppIdentity.CompiledConfiguration"/>: the values
    /// restricted to the Compile level by a "$LOCK" or a package) are added as the last source: nothing outside the build
    /// changes them. A value of the other sources that differs from a burnt one, or that a lock covers without being burnt,
    /// is ignored with a warning (the deployed configuration lags behind the build). The values are never logged (a
    /// connection string is a secret).
    /// </summary>
    internal static void ApplyCompiledConfiguration( IActivityMonitor monitor, ConfigurationManager configuration, IStObjMap map )
    {
        if( map.StObjs.Obtain( typeof( CK.AppIdentity.CompiledConfiguration ) ) is not CK.AppIdentity.CompiledConfiguration compiled
            || (compiled.Values.Count == 0 && compiled.Locks.Count == 0) )
        {
            return;
        }
        var burnt = new Dictionary<string, string?>( compiled.Values, StringComparer.OrdinalIgnoreCase );
        foreach( var (path, value) in compiled.Values )
        {
            var actual = configuration[path];
            if( actual != null && actual != value )
            {
                monitor.Warn( $"The configuration value '{path}' is compile time here: the configured value is ignored (rebuild to change it)." );
            }
        }
        foreach( var (path, value) in configuration.AsEnumerable() )
        {
            if( value == null || burnt.ContainsKey( path ) || !compiled.IsLocked( path ) ) continue;
            monitor.Warn( $"The configuration value '{path}' is locked by the build: it is ignored." );
            burnt[path] = null;
        }
        configuration.AddInMemoryCollection( burnt );
        monitor.Info( $"{compiled.Values.Count} compile time configuration value(s) applied." );
    }

    /// <summary>
    /// Adds a file of secrets (<see cref="ConfigurationSecret.TryReadFile"/>: its root is the content of the "Secrets"
    /// section) as a configuration source. A missing file adds nothing, an invalid one throws.
    /// </summary>
    internal static void AddSecretsFile( IActivityMonitor monitor, ConfigurationManager configuration, NormalizedPath file )
    {
        if( !ConfigurationSecret.TryReadFile( file, out var secrets, out var error ) ) Throw.InvalidOperationException( error );
        if( secrets.Count == 0 ) return;
        configuration.AddInMemoryCollection( secrets.Select( kv => new KeyValuePair<string, string?>( $"{ConfigurationSecret.SectionName}:{kv.Key}", kv.Value ) ) );
        monitor.Info( $"{secrets.Count} secret(s) read from '{file}'." );
    }

    /// <summary>
    /// Resolves the secret handles (<c>{ "$SECRET": "&lt;Name&gt;" }</c>, see <see cref="ConfigurationSecret"/>): the value of
    /// "&lt;path&gt;:$SECRET" is the name of a secret whose value, the configuration key "Secrets:&lt;Name&gt;" (an environment
    /// variable "Secrets__Name", a vault added by the configure action, the "#Dev/Secrets.json" of the solution in
    /// development), becomes the value of "&lt;path&gt;" through a last configuration source. A missing secret throws (all the
    /// missing names at once). The values are never logged.
    /// </summary>
    internal static void ApplySecrets( IActivityMonitor monitor, ConfigurationManager configuration )
    {
        List<KeyValuePair<string, string?>>? resolved = null;
        SortedSet<string>? missing = null;
        foreach( var (path, name) in configuration.AsEnumerable() )
        {
            if( name == null || !ConfigurationSecret.IsHandlePath( path, out var valuePath ) ) continue;
            if( !ConfigurationSecret.IsValidName( name ) )
            {
                Throw.InvalidOperationException( $"Invalid secret name '{name}' at '{path}': a name is made of segments separated by ':' made of letters, digits, '_' and '-'." );
            }
            var value = configuration[$"{ConfigurationSecret.SectionName}:{name}"];
            if( value == null ) (missing ??= new SortedSet<string>( StringComparer.OrdinalIgnoreCase )).Add( name );
            else (resolved ??= new List<KeyValuePair<string, string?>>()).Add( new KeyValuePair<string, string?>( valuePath, value ) );
        }
        if( missing != null )
        {
            Throw.InvalidOperationException( $"Missing secret(s) '{missing.Concatenate( "', '" )}': the value of a secret is the configuration key \"{ConfigurationSecret.SectionName}:<Name>\" (an environment variable \"{ConfigurationSecret.SectionName}__Name\", a vault, or \"#Dev/{ConfigurationSecret.DevFileName}\" in development)." );
        }
        if( resolved != null )
        {
            configuration.AddInMemoryCollection( resolved );
            monitor.Info( $"{resolved.Count} secret(s) resolved." );
        }
    }

    /// <summary>
    /// The configuration of a fleet (a template: "InstanceCount" or "Instances" at its root) is the one of an instance: the
    /// "Instance" key (the command line "--Instance=Red", or an environment variable) is its name or its 1-based index,
    /// and the placeholders of the values (<c>{Index}</c>, <c>{Name}</c>, <c>{n+Index}</c>, <c>{Trolley.Name}</c>
    /// where "Trolley" is the name of the App: the host assembly "Trolley.Host" without ".Host") are resolved by a last
    /// configuration source (see <see cref="ConfigurationTemplate"/>). An Opened fleet ("MaxInstanceCount") also accepts
    /// the index of a dynamic instance, after the configured ones (its name is its index). A missing or unknown instance
    /// in a fleet, an "Instance" in a host that is not a fleet, are errors.
    /// </summary>
    internal static void ApplyInstance( IActivityMonitor monitor, ConfigurationManager configuration, Assembly assembly )
    {
        const string root = "Host";
        var snapshot = new MutableConfigurationSection( root );
        snapshot.Add( configuration.AsEnumerable().Where( kv => kv.Value != null ) );
        var section = new ImmutableConfigurationSection( snapshot );
        var appName = assembly.GetName().Name ?? "";
        if( appName.EndsWith( ".Host", StringComparison.OrdinalIgnoreCase ) ) appName = appName[..^5];
        var key = configuration["Instance"];
        if( !ConfigurationTemplate.TryReadInstances( monitor, section, appName, out var instances ) )
        {
            Throw.InvalidOperationException( "Invalid instances of the host: see the logs." );
        }
        if( instances == null )
        {
            if( key != null )
            {
                Throw.InvalidOperationException( $"The host is started with the instance '{key}' but its configuration has no \"InstanceCount\", \"Instances\" nor \"MaxInstanceCount\": it is not a fleet." );
            }
            return;
        }
        var instance = key != null ? instances.Find( key ) : null;
        if( instance == null )
        {
            var known = instances.Count > 0 ? $"Its instances are: '{instances.Select( i => i.Name ).Concatenate( "', '" )}'" : "It has no configured instance";
            var dynamic = instances.IsOpened && instances.MaxInstanceCount > instances.Count
                            ? instances.MaxInstanceCount == instances.Count + 1
                                ? $", and the dynamic instance {instances.MaxInstanceCount}"
                                : $", and the dynamic instances {instances.Count + 1} to {instances.MaxInstanceCount}"
                            : "";
            Throw.InvalidOperationException( $"{(key == null ? "The host is a fleet: its instance must be specified" : $"Unknown instance '{key}'")} (the \"Instance\" configuration key, \"--Instance=Name\" on the command line). {known}{dynamic}." );
        }
        var changes = ConfigurationTemplate.ResolveSection( monitor, section, instance )
                      ?? Throw.InvalidOperationException<List<KeyValuePair<string, string?>>>( $"Unable to resolve the configuration of the {instance}: see the logs." );
        monitor.Info( $"Running the {instance}: {changes.Count} configuration value(s) resolved." );
        var prefix = root + ":";
        configuration.AddInMemoryCollection( changes.Select( kv => new KeyValuePair<string, string?>( kv.Key[prefix.Length..], kv.Value ) ) );
    }
}
