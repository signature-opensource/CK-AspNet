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
/// An invalid configuration is an error (the proxies are never trusted by default: "KnownProxies" or "KnownNetworks" is
/// required): its model is <see cref="ForwardedHeadersOptionConfiguration"/>.
/// </summary>
[PartyFeatureOption( "ForwardedHeaders", TypeActivation.Optional )]
[Configuration<ForwardedHeadersOptionConfiguration>]
public sealed class ForwardedHeadersOption : HttpEndpointOption
{
    /// <summary>
    /// Initializes a new <see cref="ForwardedHeadersOption"/> from its (checked) configuration.
    /// </summary>
    /// <param name="configuration">The configuration of the option.</param>
    public ForwardedHeadersOption( ForwardedHeadersOptionConfiguration configuration )
    {
        var options = new ForwardedHeadersOptions { ForwardedHeaders = configuration.EffectiveHeaders };
        // Nothing is trusted by default: the configured proxies replace the loopback.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach( var p in ForwardedHeadersOptionConfiguration.Split( configuration.KnownProxies ) ) options.KnownProxies.Add( IPAddress.Parse( p ) );
        foreach( var n in ForwardedHeadersOptionConfiguration.Split( configuration.KnownNetworks ) ) options.KnownIPNetworks.Add( System.Net.IPNetwork.Parse( n ) );
        if( configuration.ForwardLimit != null ) options.ForwardLimit = configuration.ForwardLimit;
        Options = options;
    }

    /// <summary>
    /// Gets the options of the middleware.
    /// </summary>
    public ForwardedHeadersOptions Options { get; }

    /// <inheritdoc />
    public override void ConfigurePipeline( IApplicationBuilder app ) => app.UseForwardedHeaders( Options );
}

/// <summary>
/// The configuration of the <see cref="ForwardedHeadersOption"/>.
/// </summary>
[ConfigurationModel]
public sealed class ForwardedHeadersOptionConfiguration : IConfigurationModel
{
    /// <summary>
    /// Gets the IP addresses of the trusted proxies (an entry may hold several addresses separated by ';').
    /// </summary>
    public IReadOnlyList<string>? KnownProxies { get; init; }

    /// <summary>
    /// Gets the CIDR networks of the trusted proxies, like "10.0.0.0/8" (an entry may hold several networks separated by ';').
    /// </summary>
    public IReadOnlyList<string>? KnownNetworks { get; init; }

    /// <summary>
    /// Gets the forwarded headers: a combination of XForwardedFor, XForwardedHost, XForwardedProto and XForwardedPrefix
    /// (separated by ','). Defaults to "XForwardedFor, XForwardedProto, XForwardedHost".
    /// </summary>
    public string? Headers { get; init; }

    /// <summary>
    /// Gets the number of proxies to go through (defaults to 1, the default of ASP.NET).
    /// </summary>
    public int? ForwardLimit { get; init; }

    /// <summary>
    /// Gets the headers to process.
    /// </summary>
    public ForwardedHeaders EffectiveHeaders => Headers != null && Enum.TryParse<ForwardedHeaders>( Headers, ignoreCase: true, out var h )
                                                    ? h
                                                    : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;

    /// <summary>
    /// Splits the entries of a list on their ';'.
    /// </summary>
    /// <param name="entries">The entries.</param>
    /// <returns>The values.</returns>
    public static IEnumerable<string> Split( IReadOnlyList<string>? entries )
        => entries?.SelectMany( e => e.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) ) ?? [];

    /// <inheritdoc />
    public void Validate( IConfigurationValidationContext context )
    {
        var proxies = Split( KnownProxies ).ToList();
        var networks = Split( KnownNetworks ).ToList();
        foreach( var p in proxies.Where( p => !IPAddress.TryParse( p, out _ ) ) )
        {
            context.Error( $"'{context.Path}:KnownProxies': '{p}' is not an IP address." );
        }
        foreach( var n in networks.Where( n => !System.Net.IPNetwork.TryParse( n, out _ ) ) )
        {
            context.Error( $"'{context.Path}:KnownNetworks': '{n}' is not a CIDR network (like '10.0.0.0/8')." );
        }
        if( proxies.Count == 0 && networks.Count == 0 )
        {
            context.Error( $"'{context.Path}' must have \"KnownProxies\" or \"KnownNetworks\": the proxies are never trusted by default." );
        }
        if( Headers != null && (!Enum.TryParse<ForwardedHeaders>( Headers, ignoreCase: true, out var h ) || h == ForwardedHeaders.None) )
        {
            context.Error( $"'{context.Path}:Headers' is '{Headers}': a combination of XForwardedFor, XForwardedHost, XForwardedProto and XForwardedPrefix is expected." );
        }
        if( ForwardLimit is <= 0 )
        {
            context.Error( $"'{context.Path}:ForwardLimit' is {ForwardLimit}: a positive number is expected." );
        }
    }
}
