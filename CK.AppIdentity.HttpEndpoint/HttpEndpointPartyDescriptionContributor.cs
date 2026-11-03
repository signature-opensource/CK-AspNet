using CK.Core;
using System;

namespace CK.AppIdentity;

/// <summary>
/// Contributes the public URL of the root party (its <see cref="IParty.Description"/>, see the "PartyDescription" feature
/// of CK.AppIdentity) when "Local:PublicUrl" is not configured: the <see cref="RunningHttpEndpoint.DefaultUrl"/> of the
/// first started endpoint (a port 0 is resolved). Behind a reverse proxy or a public domain name, "Local:PublicUrl" is
/// configured.
/// </summary>
public sealed class HttpEndpointPartyDescriptionContributor : IPartyDescriptionContributor
{
    readonly HttpEndpointFeatureDriver _driver;

    /// <summary>
    /// Initializes a new <see cref="HttpEndpointPartyDescriptionContributor"/>.
    /// </summary>
    /// <param name="driver">The HttpEndpoint feature driver.</param>
    public HttpEndpointPartyDescriptionContributor( HttpEndpointFeatureDriver driver )
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
    public void Contribute( PartyDescription.Builder builder )
    {
        if( builder.PublicUrl != null ) return;
        foreach( var e in _driver.Endpoints )
        {
            if( e.DefaultUrl != null
                && PartyDescription.TryParsePublicUrl( e.DefaultUrl, out var url, out _ ) )
            {
                builder.PublicUrl = url;
                return;
            }
        }
    }
}
