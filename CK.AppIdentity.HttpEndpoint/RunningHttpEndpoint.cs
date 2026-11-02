using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;

namespace CK.AppIdentity;

/// <summary>
/// A running endpoint of the <see cref="HttpEndpointFeatureDriver"/>: its web application.
/// </summary>
public sealed class RunningHttpEndpoint
{
    internal RunningHttpEndpoint( string name, WebApplication application )
    {
        Name = name;
        Application = application;
        var addresses = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        Addresses = addresses != null ? [.. addresses.Addresses] : [];
    }

    /// <summary>
    /// Gets the name of this endpoint (its "Local:HttpEndpoint" section).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the web application of this endpoint.
    /// </summary>
    public WebApplication Application { get; }

    /// <summary>
    /// Gets the actual addresses of this endpoint (a port 0 is resolved).
    /// </summary>
    public IReadOnlyList<string> Addresses { get; }

    /// <summary>
    /// Overridden to return the name and the addresses.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"HttpEndpoint '{Name}' ({string.Join( ", ", Addresses )})";
}
