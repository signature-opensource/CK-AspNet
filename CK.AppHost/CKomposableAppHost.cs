using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using System;
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
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    /// <param name="configure">Optional configuration of the builder, called right after its creation.</param>
    /// <returns>The awaitable.</returns>
    public static async Task RunAsync( string[] args, Action<HostApplicationBuilder>? configure = null )
    {
        using var host = Build( args, configure );
        await host.RunAsync().ConfigureAwait( false );
    }

    /// <summary>
    /// Builds the host, not started: <see cref="RunAsync(string[], Action{HostApplicationBuilder}?)"/> uses it, and the tests
    /// use it to start and stop the host themselves.
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    /// <param name="configure">
    /// Optional configuration of the builder, called right after its creation (before the monitoring and the application
    /// identity are configured): the tests use it to override the configuration.
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
                               Action<HostApplicationBuilder>? configure = null,
                               string? contentRoot = null,
                               Assembly? assembly = null )
    {
        assembly ??= Assembly.GetEntryAssembly()
                     ?? Throw.InvalidOperationException<Assembly>( "No entry assembly: the assembly that contains the generated code must be specified." );
        contentRoot ??= WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null;
        var builder = Host.CreateApplicationBuilder( new HostApplicationBuilderSettings { Args = args, ContentRootPath = contentRoot } );
        configure?.Invoke( builder );
        // Both do nothing when the process doesn't run as a service.
        builder.Services.AddWindowsService();
        builder.Services.AddSystemd();
        builder.UseCKMonitoring();
        builder.AddApplicationIdentityServiceConfiguration();
        // The monitor of the work outside the requests (the requests of the HttpEndpoints have the one of their ScopedHttpContext).
        builder.Services.AddScoped<IActivityMonitor, ActivityMonitor>();
        builder.Services.AddScoped( sp => sp.GetRequiredService<IActivityMonitor>().ParallelLogger );
        var monitor = builder.GetBuilderMonitor();
        var map = StObjContextRoot.Load( assembly, monitor )
                  ?? Throw.InvalidOperationException<IStObjMap>( $"Unable to load the generated code of '{assembly.GetName().Name}': build its Builder." );
        builder.Services.AddStObjMap( monitor, map );
        return builder.CKBuild();
    }
}
