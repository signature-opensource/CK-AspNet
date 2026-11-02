using CK.AppIdentity;
using CK.Core;
using Microsoft.AspNetCore.Builder;
using System;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Adds the application's configuration of an endpoint of the "HttpEndpoint" feature.
/// </summary>
public static class HttpEndpointHostApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the application's configuration of the <paramref name="name"/> endpoint: its services and its pipeline, in
    /// one gesture on the <see cref="WebApplicationBuilder"/> of the endpoint (its middlewares are added with
    /// <c>AppendApplicationBuilder</c> or <c>PrependApplicationBuilder</c>, applied by <c>CKBuild</c>).
    /// <para>
    /// The endpoint itself (its addresses) is configured by the "Local:HttpEndpoint:<paramref name="name"/>" section of
    /// the "CK-AppIdentity" configuration: a configuration without this section is an error.
    /// </para>
    /// </summary>
    /// <typeparam name="T">This builder's type.</typeparam>
    /// <param name="builder">This builder.</param>
    /// <param name="name">The name of the endpoint.</param>
    /// <param name="configure">The configuration of the endpoint's web application.</param>
    /// <returns>This builder.</returns>
    public static T AddHttpEndpoint<T>( this T builder, string name, Action<WebApplicationBuilder>? configure = null ) where T : IHostApplicationBuilder
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( name );
        HttpEndpointHost.FindOrAdd( builder.Services ).AddConfiguration( name, configure );
        return builder;
    }
}
