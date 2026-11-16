using CK.Core;
using Microsoft.AspNetCore.Builder;

namespace CK.AppIdentity;

/// <summary>
/// The base class of the options of the <see cref="HttpEndpointFeatureDriver"/>: what the endpoint adds to its web
/// application, enabled or disabled by the "Local:HttpEndpoint" section of the party (its key in this section is the
/// option name declared by its <see cref="PartyFeatureOptionAttribute"/>).
/// <para>
/// A concrete option is decorated with <see cref="PartyFeatureOptionAttribute"/> and has one public constructor: its
/// <c>IConfiguration</c> parameter is its section (null when it is enabled by default or by <c>true</c>), read into its
/// properties (an invalid value throws), the other ones are resolved from the services of the host. The topology attributes between options (<c>[Requires&lt;T&gt;]</c>,
/// <c>[RequiredBy&lt;T&gt;]</c> and their optional forms) order them; a non optional relation also means "only with it".
/// </para>
/// <para>
/// An instance is created before the feature is set up when the party enables the option (an invalid configuration fails
/// the setup), then <see cref="ConfigureServices"/> and <see cref="ConfigurePipeline"/> are called when the endpoint starts.
/// </para>
/// </summary>
public abstract class HttpEndpointOption : IPartyComponentOption<HttpEndpointFeatureDriver>
{
    /// <summary>
    /// Configures the services of the web application of the endpoint. Does nothing by default.
    /// </summary>
    /// <param name="builder">The builder of the web application.</param>
    public virtual void ConfigureServices( WebApplicationBuilder builder )
    {
    }

    /// <summary>
    /// Adds the middlewares of this option to the pipeline of the endpoint. The options' middlewares are the first ones of
    /// the pipeline (before the CKMiddleware), in the order of the options. Does nothing by default.
    /// </summary>
    /// <param name="app">The application builder.</param>
    public virtual void ConfigurePipeline( IApplicationBuilder app )
    {
    }
}
