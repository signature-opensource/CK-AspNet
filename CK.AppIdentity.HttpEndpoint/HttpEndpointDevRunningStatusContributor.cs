using CK.Core;
using System;
using System.Text.Json;

namespace CK.AppIdentity;

/// <summary>
/// Contributes the "Http" section of the running status of a development party (the "DevRunningStatus" feature of
/// CK.AppIdentity): the <see cref="HttpEndpointFeatureDriver.Endpoints"/> with their "Name", "Urls" and "DefaultUrl"
/// (see <see cref="RunningHttpEndpoint"/>).
/// <para>
/// The party is ready once the endpoints are started (or when this feature has nothing to start).
/// </para>
/// </summary>
public sealed class HttpEndpointDevRunningStatusContributor : IDevRunningStatusContributor
{
    readonly HttpEndpointFeatureDriver _driver;

    /// <summary>
    /// Initializes a new <see cref="HttpEndpointDevRunningStatusContributor"/>.
    /// </summary>
    /// <param name="driver">The HttpEndpoint feature driver.</param>
    public HttpEndpointDevRunningStatusContributor( HttpEndpointFeatureDriver driver )
    {
        _driver = driver;
    }

    /// <inheritdoc />
    public void Activate( IActivityMonitor monitor, Action onChanged )
    {
        // The static features are started in any order: the endpoints may be started before or after this.
        _driver.EndpointsChanged += onChanged;
    }

    /// <inheritdoc />
    public bool IsReady => _driver.IsInactiveOrStarted;

    /// <inheritdoc />
    public void Write( Utf8JsonWriter writer )
    {
        writer.WriteStartArray( "Http" );
        foreach( var e in _driver.Endpoints )
        {
            writer.WriteStartObject();
            writer.WriteString( "Name", e.Name );
            writer.WriteStartArray( "Urls" );
            foreach( var u in e.Urls ) writer.WriteStringValue( u );
            writer.WriteEndArray();
            writer.WriteString( "DefaultUrl", e.DefaultUrl );
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}
