using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.AppIdentity;

/// <summary>
/// A running endpoint of the <see cref="HttpEndpointFeatureDriver"/>: its web application.
/// </summary>
public sealed class RunningHttpEndpoint
{
    internal RunningHttpEndpoint( string name, WebApplication application, bool configuredByUrls )
    {
        Name = name;
        Application = application;
        var addresses = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        Addresses = addresses != null ? [.. addresses.Addresses] : [];
        // Kestrel reports its addresses in their binding order: with "Urls", the first one is the first configured URL.
        // The endpoints of a "Kestrel" section are sorted by their name (the configuration sorts the keys): the first
        // https one is preferred.
        var primary = configuredByUrls
                        ? Addresses.FirstOrDefault()
                        : Addresses.FirstOrDefault( a => a.StartsWith( "https://", StringComparison.OrdinalIgnoreCase ) ) ?? Addresses.FirstOrDefault();
        PrimaryAddress = primary != null ? ToBrowsable( primary ) : null;
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
    /// Gets the address to use to reach this endpoint (a browser, a status): the actual address of the first configured
    /// URL (for a "Kestrel" section, the first https address, otherwise the first one), with "localhost" instead of a
    /// wildcard host ("*", "+", "0.0.0.0" or "[::]"). Null if the endpoint has no address.
    /// <para>
    /// This is the address the endpoint binds: behind a reverse proxy or in a container, the public address differs.
    /// </para>
    /// </summary>
    public string? PrimaryAddress { get; }

    /// <summary>
    /// Overridden to return the name and the addresses.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"HttpEndpoint '{Name}' ({string.Join( ", ", Addresses )})";

    static string ToBrowsable( string address )
    {
        int hostStart = address.IndexOf( "://", StringComparison.Ordinal );
        if( hostStart < 0 ) return address;
        hostStart += 3;
        int hostEnd = address.Length > hostStart && address[hostStart] == '['
                        ? address.IndexOf( ']', hostStart ) + 1
                        : address.IndexOfAny( [':', '/'], hostStart );
        if( hostEnd <= 0 ) hostEnd = address.Length;
        var host = address.Substring( hostStart, hostEnd - hostStart );
        return host is "*" or "+" or "0.0.0.0" or "[::]"
                ? string.Concat( address.AsSpan( 0, hostStart ), "localhost", address.AsSpan( hostEnd ) )
                : address;
    }
}
