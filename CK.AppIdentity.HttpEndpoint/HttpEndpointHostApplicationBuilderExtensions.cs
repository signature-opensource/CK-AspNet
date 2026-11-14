using CK.AppIdentity;
using CK.Core;
using Microsoft.AspNetCore.Builder;
using System;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Adds the application's configuration of the endpoint of the "HttpEndpoint" feature.
/// </summary>
public static class HttpEndpointHostApplicationBuilderExtensions
{
    /// <summary>
    /// Adds an application's configuration of the endpoint: its services and its pipeline, in one gesture on the
    /// <see cref="WebApplicationBuilder"/> of the endpoint (its middlewares are added with <c>AppendApplicationBuilder</c>
    /// or <c>PrependApplicationBuilder</c>, applied by <c>CKBuild</c>). This can be called more than once: the
    /// configurations are applied in their order, after the contributions of the packages.
    /// <para>
    /// The endpoint itself (its addresses) is configured by the "Local:HttpEndpoint" section of the root party.
    /// </para>
    /// </summary>
    /// <param name="builder">The builder of the host.</param>
    /// <param name="configure">The configuration of the endpoint's web application.</param>
    /// <returns>This builder.</returns>
    public static HostApplicationBuilder ConfigureHttpEndpoint( this HostApplicationBuilder builder, Action<WebApplicationBuilder> configure )
    {
        Throw.CheckNotNullArgument( configure );
        HttpEndpointHost.FindOrAdd( builder.Services ).AddConfiguration( configure );
        return builder;
    }
}