using Microsoft.Extensions.Hosting;
using System;

namespace CK.Core;

/// <summary>
/// Runs the host instead of <see cref="CKomposableAppHost"/>: a package that changes how the process runs (a desktop
/// window that shows a client application of the party, for instance) implements it. It is a real object: the generated
/// code has it before the host is built, so it is found by <see cref="CKomposableAppHost.Run(string[])"/> even when the
/// host fails to build. An App has at most one runner (the engine refuses two implementations).
/// <para>
/// Without a runner, the host runs until it stops (<c>host.Run()</c>).
/// </para>
/// </summary>
public interface IAppHostRunner : IRealObject
{
    /// <summary>
    /// Called while the host is built, right after the creation of the builder (before the monitoring and the application
    /// identity are configured, like the configure action of <see cref="CKomposableAppHost.Build"/>). Does nothing by
    /// default.
    /// </summary>
    /// <param name="monitor">The monitor of the builder.</param>
    /// <param name="builder">The builder of the host.</param>
    void ConfigureHost( IActivityMonitor monitor, HostApplicationBuilder builder ) { }

    /// <summary>
    /// Runs the host (built, not started) until it stops, on the thread of <see cref="CKomposableAppHost.Run(string[])"/>:
    /// the main thread of the process. A failure must be handled: an exception that escapes is reported and the exit code
    /// is 1.
    /// </summary>
    /// <param name="host">The host to run.</param>
    /// <returns>The exit code of the process.</returns>
    int Run( IHost host );

    /// <summary>
    /// Called when the host failed to build, after the failure is reported (the standard error and the logs): a runner
    /// may show it (a desktop application has no console). Does nothing by default.
    /// </summary>
    /// <param name="ex">The failure.</param>
    void OnBuildFailure( Exception ex ) { }
}
