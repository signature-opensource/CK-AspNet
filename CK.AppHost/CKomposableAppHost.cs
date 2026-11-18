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
        var builder = Host.CreateApplicationBuilder( new HostApplicationBuilderSettings { Args = args, ContentRootPath = contentRoot } );
        var monitor = builder.GetBuilderMonitor();
        var map = StObjContextRoot.Load( assembly, monitor )
                  ?? Throw.InvalidOperationException<IStObjMap>( $"Unable to load the generated code of '{assembly.GetName().Name}': build its Builder." );

        configure?.Invoke( monitor, builder, map );

        // A fleet: the instance is chosen before anything reads the configuration.
        ApplyInstance( monitor, builder.Configuration, assembly );

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
    /// The configuration of a fleet (a template: "InstanceCount" or "Instances" at its root) is the one of an instance: the
    /// "Instance" key (the command line "--Instance=Red", or an environment variable) is its name or its 1-based index,
    /// and the placeholders of the values (<c>{Index}</c>, <c>{Name}</c>, <c>{n+Index}</c>, <c>{Trolley.Name}</c>
    /// where "Trolley" is the name of the App: the host assembly "Trolley.Host" without ".Host") are resolved by a last
    /// configuration source (see <see cref="ConfigurationTemplate"/>). A missing or unknown instance in a fleet, an
    /// "Instance" in a host that is not a fleet, are errors.
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
                Throw.InvalidOperationException( $"The host is started with the instance '{key}' but its configuration has no \"InstanceCount\" nor \"Instances\": it is not a fleet." );
            }
            return;
        }
        var instance = key != null ? ConfigurationTemplate.FindInstance( instances, key ) : null;
        if( instance == null )
        {
            Throw.InvalidOperationException( $"{(key == null ? "The host is a fleet: its instance must be specified" : $"Unknown instance '{key}'")} (the \"Instance\" configuration key, \"--Instance=Name\" on the command line). Its instances are: '{instances.Select( i => i.Name ).Concatenate( "', '" )}'." );
        }
        var changes = ConfigurationTemplate.ResolveSection( monitor, section, instance )
                      ?? Throw.InvalidOperationException<List<KeyValuePair<string, string?>>>( $"Unable to resolve the configuration of the {instance}: see the logs." );
        monitor.Info( $"Running the {instance}: {changes.Count} configuration value(s) resolved." );
        var prefix = root + ":";
        configuration.AddInMemoryCollection( changes.Select( kv => new KeyValuePair<string, string?>( kv.Key[prefix.Length..], kv.Value ) ) );
    }
}
