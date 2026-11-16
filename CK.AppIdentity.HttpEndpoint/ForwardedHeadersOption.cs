using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
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
/// An invalid configuration throws (the proxies are never trusted by default: a section with "KnownProxies" or
/// "KnownNetworks" is required).
/// </summary>
[PartyFeatureOption( "ForwardedHeaders", TypeActivation.Optional )]
public sealed class ForwardedHeadersOption : HttpEndpointOption
{
    static readonly string[] _keys = ["KnownProxies", "KnownNetworks", "Headers", "ForwardLimit"];

    /// <summary>
    /// Initializes a new <see cref="ForwardedHeadersOption"/> from its configuration.
    /// </summary>
    /// <param name="configuration">The "ForwardedHeaders" section of the endpoint.</param>
    public ForwardedHeadersOption( IConfiguration? configuration )
    {
        if( configuration == null )
        {
            Throw.CKException( "It must be a section with \"KnownProxies\" or \"KnownNetworks\": the proxies are never trusted by default." );
        }
        foreach( var c in configuration.GetChildren() )
        {
            if( !_keys.Contains( c.Key ) )
            {
                Throw.CKException( $"Unknown \"{c.Key}\": the keys are {_keys.Concatenate()}." );
            }
        }
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
        };
        // Nothing is trusted by default: the configured proxies replace the loopback.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach( var p in ReadList( configuration, "KnownProxies" ) )
        {
            if( !IPAddress.TryParse( p, out var a ) ) Throw.CKException( $"Invalid \"KnownProxies\": '{p}' is not an IP address." );
            options.KnownProxies.Add( a );
        }
        foreach( var n in ReadList( configuration, "KnownNetworks" ) )
        {
            if( !System.Net.IPNetwork.TryParse( n, out var net ) ) Throw.CKException( $"Invalid \"KnownNetworks\": '{n}' is not a CIDR network (like '10.0.0.0/8')." );
            options.KnownIPNetworks.Add( net );
        }
        if( options.KnownProxies.Count == 0 && options.KnownIPNetworks.Count == 0 )
        {
            Throw.CKException( "It must have \"KnownProxies\" or \"KnownNetworks\": the proxies are never trusted by default." );
        }
        var headers = configuration["Headers"];
        if( headers != null )
        {
            if( !Enum.TryParse<ForwardedHeaders>( headers, ignoreCase: true, out var h ) || h == ForwardedHeaders.None )
            {
                Throw.CKException( $"Invalid \"Headers\": '{headers}'. Expected a combination of XForwardedFor, XForwardedHost, XForwardedProto and XForwardedPrefix." );
            }
            options.ForwardedHeaders = h;
        }
        var limit = configuration["ForwardLimit"];
        if( limit != null )
        {
            if( !int.TryParse( limit, out var l ) || l <= 0 ) Throw.CKException( $"Invalid \"ForwardLimit\": '{limit}' (a positive number is expected)." );
            options.ForwardLimit = l;
        }
        Options = options;

        static IEnumerable<string> ReadList( IConfiguration c, string key )
        {
            var value = c[key];
            return value != null
                    ? value.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries )
                    : c.GetSection( key ).GetChildren()
                                         .Select( v => v.Value )
                                         .Where( v => !string.IsNullOrWhiteSpace( v ) )
                                         .Select( v => v!.Trim() );
        }
    }

    /// <summary>
    /// Gets the options of the middleware.
    /// </summary>
    public ForwardedHeadersOptions Options { get; }

    /// <inheritdoc />
    public override void ConfigurePipeline( IApplicationBuilder app ) => app.UseForwardedHeaders( Options );
}
