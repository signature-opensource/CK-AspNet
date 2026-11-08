using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace CK.AppIdentity;

/// <summary>
/// The forwarded headers of an endpoint behind a reverse proxy: the client's scheme, host and address are read from the
/// X-Forwarded-Proto, X-Forwarded-Host and X-Forwarded-For headers sent by the trusted proxies, so that everything in the
/// pipeline (the logs, the same origin checks of the authentication) sees the client.
/// <para>
/// It fails closed: the proxies are trusted explicitly (<c>KnownProxies</c>, <c>KnownNetworks</c>), nothing is trusted
/// by default (not even the loopback).
/// </para>
/// <code>
/// "ForwardedHeaders": {
///   "KnownProxies": "10.0.0.12",          // IP addresses, separated by ';' (or an array).
///   "KnownNetworks": "10.0.0.0/8",        // CIDR networks, separated by ';' (or an array).
///   "Headers": "XForwardedFor, XForwardedProto, XForwardedHost", // The default.
///   "ForwardLimit": 1                     // The default: the number of proxies to go through.
/// }
/// </code>
/// </summary>
[FeatureOption]
public sealed class ForwardedHeadersOption : HttpEndpointOption
{
    ForwardedHeadersOptions? _options;

    /// <summary>
    /// Gets the options of the middleware (null until <see cref="Initialize"/> succeeds).
    /// </summary>
    public ForwardedHeadersOptions? Options => _options;

    /// <inheritdoc />
    public override bool Initialize( IActivityMonitor monitor, ImmutableConfigurationSection endpoint, ImmutableConfigurationSection? configuration )
    {
        var path = $"{endpoint.Path}:ForwardedHeaders";
        if( configuration == null )
        {
            monitor.Error( $"\"{path}\" must be a section with \"KnownProxies\" or \"KnownNetworks\": the proxies are never trusted by default." );
            return false;
        }
        bool success = true;
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
        };
        // Nothing is trusted by default: the configured proxies replace the loopback.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach( var p in ReadList( configuration, "KnownProxies" ) )
        {
            if( IPAddress.TryParse( p, out var a ) ) options.KnownProxies.Add( a );
            else
            {
                monitor.Error( $"Invalid \"{path}:KnownProxies\": '{p}' is not an IP address." );
                success = false;
            }
        }
        foreach( var n in ReadList( configuration, "KnownNetworks" ) )
        {
            if( System.Net.IPNetwork.TryParse( n, out var net ) ) options.KnownIPNetworks.Add( net );
            else
            {
                monitor.Error( $"Invalid \"{path}:KnownNetworks\": '{n}' is not a CIDR network (like '10.0.0.0/8')." );
                success = false;
            }
        }
        if( success && options.KnownProxies.Count == 0 && options.KnownIPNetworks.Count == 0 )
        {
            monitor.Error( $"\"{path}\" must have \"KnownProxies\" or \"KnownNetworks\": the proxies are never trusted by default." );
            success = false;
        }
        var headers = configuration["Headers"];
        if( headers != null )
        {
            if( Enum.TryParse<ForwardedHeaders>( headers, ignoreCase: true, out var h ) && h != ForwardedHeaders.None )
            {
                options.ForwardedHeaders = h;
            }
            else
            {
                monitor.Error( $"Invalid \"{path}:Headers\": '{headers}'. Expected a combination of XForwardedFor, XForwardedHost, XForwardedProto and XForwardedPrefix." );
                success = false;
            }
        }
        var limit = configuration["ForwardLimit"];
        if( limit != null )
        {
            if( int.TryParse( limit, out var l ) && l > 0 ) options.ForwardLimit = l;
            else
            {
                monitor.Error( $"Invalid \"{path}:ForwardLimit\": '{limit}' (a positive number is expected)." );
                success = false;
            }
        }
        foreach( var c in configuration.GetChildren() )
        {
            if( c.Key is not ("KnownProxies" or "KnownNetworks" or "Headers" or "ForwardLimit") )
            {
                monitor.Error( $"Unknown \"{c.Path}\": the keys of \"{path}\" are KnownProxies, KnownNetworks, Headers and ForwardLimit." );
                success = false;
            }
        }
        if( success ) _options = options;
        return success;

        static IEnumerable<string> ReadList( ImmutableConfigurationSection c, string key )
        {
            var value = c[key];
            return value != null
                    ? value.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries )
                    : c.TryGetSection( key )?.GetChildren()
                                             .Select( v => v.Value )
                                             .Where( v => !string.IsNullOrWhiteSpace( v ) )
                                             .Select( v => v!.Trim() )
                      ?? [];
        }
    }

    /// <inheritdoc />
    public override void ConfigurePipeline( IApplicationBuilder app )
    {
        Throw.CheckState( _options != null );
        app.UseForwardedHeaders( _options );
    }
}
