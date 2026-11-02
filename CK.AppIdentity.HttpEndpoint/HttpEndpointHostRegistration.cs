using CK.Core;

namespace CK.AppIdentity;

/// <summary>
/// Captures the services of the host for the <see cref="HttpEndpointFeatureDriver"/>: the web applications of the
/// endpoints get the services of the host (see the README).
/// <para>
/// A real object is shared by every host that loads the same map: it doesn't keep the services, it registers them
/// in the <see cref="HttpEndpointHost"/> of the host.
/// </para>
/// </summary>
public sealed class HttpEndpointHostRegistration : IRealObject
{
    void ConfigureServices( StObjContextRoot.ServiceRegister services )
    {
        HttpEndpointHost.FindOrAdd( services.Services ).SetHostServices( services.Services );
    }
}
