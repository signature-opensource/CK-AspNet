using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using System;

namespace CK.AppIdentity;

/// <summary>
/// The in-process server of an endpoint: the web application of the endpoint runs on an in-memory server
/// (<see cref="TestServer"/>) instead of Kestrel. No port is opened: the requests are sent to it by code
/// (<see cref="InProcessHttpEndpointExtensions.GetInProcessServer(RunningHttpEndpoint)"/>), for instance by a desktop
/// window that relays the requests of its page.
/// <code>
/// "HttpEndpoint": { "InProcess": true }                          // The URL is "app://localhost".
/// "HttpEndpoint": { "InProcess": { "Url": "app://localhost" } }  // An absolute URL without path.
/// </code>
/// The endpoint then has no "Urls" nor "Kestrel": this URL is its <see cref="RunningHttpEndpoint.DefaultUrl"/> (and the
/// scheme and host of its requests).
/// </summary>
[PartyFeatureOption( "InProcess", TypeActivation.Optional )]
[Configuration<InProcessHttpEndpointOptionConfiguration>]
public sealed class InProcessHttpEndpointOption : HttpEndpointOption
{
    /// <summary>
    /// The default URL: "app://localhost".
    /// </summary>
    public const string DefaultUrl = "app://localhost";

    readonly string _serverUrl;

    /// <summary>
    /// Initializes a new <see cref="InProcessHttpEndpointOption"/> from its configuration.
    /// </summary>
    /// <param name="configuration">The configuration of the option (its "Url" checked).</param>
    public InProcessHttpEndpointOption( InProcessHttpEndpointOptionConfiguration configuration )
    {
        var uri = new Uri( configuration.Url );
        BaseAddress = uri;
        _serverUrl = uri.GetLeftPart( UriPartial.Authority );
    }

    /// <summary>
    /// Gets the base address of the in-process server: its scheme and host are the ones of its requests.
    /// </summary>
    public Uri BaseAddress { get; }

    /// <summary>
    /// Gets the URL of the in-process server (its scheme and authority, like "app://localhost").
    /// </summary>
    public override string? ServerUrl => _serverUrl;

    /// <summary>
    /// Registers the in-process server.
    /// </summary>
    /// <param name="builder">The builder of the web application.</param>
    public override void ConfigureServices( WebApplicationBuilder builder )
    {
        builder.WebHost.UseTestServer( o => o.BaseAddress = BaseAddress );
    }
}

/// <summary>
/// The configuration of the <see cref="InProcessHttpEndpointOption"/>: <c>"InProcess": true</c> or
/// <c>"InProcess": { "Url": "app://localhost" }</c>.
/// </summary>
[ConfigurationModel]
public sealed class InProcessHttpEndpointOptionConfiguration : IConfigurationModel
{
    /// <summary>
    /// Gets the URL of the in-process server: an absolute URL without path, its scheme and host are the ones of the
    /// requests of the endpoint.
    /// </summary>
    public string Url { get; init; } = InProcessHttpEndpointOption.DefaultUrl;

    /// <inheritdoc />
    public void Validate( IConfigurationValidationContext context )
    {
        if( !Uri.TryCreate( Url, UriKind.Absolute, out var uri )
            || uri.AbsolutePath != "/"
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0 )
        {
            context.Error( $"'{context.Path}:Url' is '{Url}': an absolute URL without path is expected (like \"{InProcessHttpEndpointOption.DefaultUrl}\")." );
        }
    }
}
