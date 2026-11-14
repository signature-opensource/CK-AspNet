using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.AppIdentity;

/// <summary>
/// The running endpoint of the <see cref="HttpEndpointFeatureDriver"/>: its web application.
/// </summary>
public sealed class RunningHttpEndpoint
{
    internal RunningHttpEndpoint( WebApplication application, bool configuredByUrls, string? defaultEndpointUrl )
    {
        Application = application;
        var addresses = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        Urls = addresses != null ? [.. addresses.Addresses] : [];
        // Kestrel reports its URLs in their binding order: with "Urls", the first one is the first configured URL.
        // The endpoints of a "Kestrel" section are sorted by their name (the configuration sorts the keys): its
        // "DefaultEndpoint" names the one to use, otherwise the first https one is preferred.
        var defaultUrl = configuredByUrls
                            ? Urls.FirstOrDefault()
                            : (defaultEndpointUrl != null ? Match( defaultEndpointUrl, Urls ) : null)
                              ?? Urls.FirstOrDefault( u => u.StartsWith( "https://", StringComparison.OrdinalIgnoreCase ) )
                              ?? Urls.FirstOrDefault();
        DefaultUrl = defaultUrl != null ? ToBrowsable( defaultUrl ) : null;
    }

    /// <summary>
    /// Gets the web application of this endpoint.
    /// </summary>
    public WebApplication Application { get; }

    /// <summary>
    /// Gets the actual URLs of this endpoint (a port 0 is resolved).
    /// </summary>
    public IReadOnlyList<string> Urls { get; }

    /// <summary>
    /// Gets the URL to use to reach this endpoint (a browser, a status), with "localhost" instead of a wildcard host
    /// ("*", "+", "0.0.0.0" or "[::]"). Null if the endpoint has no URL:
    /// <list type="bullet">
    ///   <item>with "Urls", the actual URL of the first configured one;</item>
    ///   <item>
    ///   with a "Kestrel" section, the one of its endpoint named by "DefaultEndpoint", otherwise the first https URL,
    ///   otherwise the first one.
    ///   </item>
    /// </list>
    /// <para>
    /// This is the URL the endpoint binds: behind a reverse proxy or in a container, the public URL differs.
    /// </para>
    /// </summary>
    public string? DefaultUrl { get; }

    /// <summary>
    /// Overridden to return the URLs.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"HttpEndpoint ({string.Join( ", ", Urls )})";

    // The actual URL of a configured one: the same scheme, and the same port (or the same host when the configured port
    // is 0, the wildcard hosts being equivalent).
    static string? Match( string configured, IReadOnlyList<string> actuals )
    {
        if( !TrySplit( configured, out var scheme, out var host, out var port ) ) return null;
        foreach( var a in actuals )
        {
            if( !TrySplit( a, out var aScheme, out var aHost, out var aPort ) ) continue;
            if( !string.Equals( scheme, aScheme, StringComparison.OrdinalIgnoreCase ) ) continue;
            if( port != "0" ? port == aPort : string.Equals( Normalize( host ), Normalize( aHost ), StringComparison.OrdinalIgnoreCase ) )
            {
                return a;
            }
        }
        return null;

        static string Normalize( string host ) => IsWildcard( host ) ? "*" : host;
    }

    static string ToBrowsable( string url )
    {
        if( !TrySplit( url, out _, out var host, out _ ) || !IsWildcard( host ) ) return url;
        int hostStart = url.IndexOf( "://", StringComparison.Ordinal ) + 3;
        return string.Concat( url.AsSpan( 0, hostStart ), "localhost", url.AsSpan( hostStart + host.Length ) );
    }

    static bool IsWildcard( string host ) => host is "*" or "+" or "0.0.0.0" or "[::]";

    // "scheme://host:port/...": the host can be an IPv6 literal ("[::1]"); the port is empty when absent.
    static bool TrySplit( string url, out string scheme, out string host, out string port )
    {
        scheme = host = port = "";
        int schemeEnd = url.IndexOf( "://", StringComparison.Ordinal );
        if( schemeEnd <= 0 ) return false;
        scheme = url.Substring( 0, schemeEnd );
        int hostStart = schemeEnd + 3;
        int hostEnd = url.Length > hostStart && url[hostStart] == '['
                        ? url.IndexOf( ']', hostStart ) + 1
                        : url.IndexOfAny( [':', '/'], hostStart );
        if( hostEnd <= 0 ) hostEnd = url.Length;
        host = url.Substring( hostStart, hostEnd - hostStart );
        if( hostEnd < url.Length && url[hostEnd] == ':' )
        {
            int portEnd = url.IndexOf( '/', hostEnd );
            port = url.Substring( hostEnd + 1, (portEnd < 0 ? url.Length : portEnd) - hostEnd - 1 );
        }
        return true;
    }
}
