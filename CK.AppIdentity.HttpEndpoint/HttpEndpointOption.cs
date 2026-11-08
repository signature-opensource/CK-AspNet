using CK.Core;
using Microsoft.AspNetCore.Builder;

namespace CK.AppIdentity;

/// <summary>
/// The base class of the options of the <see cref="HttpEndpointFeatureDriver"/>: what an endpoint adds to its web
/// application, enabled or disabled by the configuration of each endpoint (its key in the endpoint section is the
/// option name: the type name without its "Option" suffix).
/// <para>
/// A concrete option is decorated with <see cref="FeatureOptionAttribute"/> and has one public constructor whose parameters
/// are resolved from the services of the host. The topology attributes between options (<c>[Requires&lt;T&gt;]</c>,
/// <c>[RequiredBy&lt;T&gt;]</c> and their optional forms) order them; a non optional relation also means "only with it".
/// </para>
/// <para>
/// An instance is created for each endpoint that enables the option: <see cref="Initialize"/> is called when the feature
/// is set up (a configuration error fails the setup), then <see cref="ConfigureServices"/> and <see cref="ConfigurePipeline"/>
/// when the endpoint starts.
/// </para>
/// </summary>
public abstract class HttpEndpointOption : IPartyComponentOption<HttpEndpointFeatureDriver>
{
    /// <summary>
    /// Reads and checks the configuration of this option for an endpoint. Does nothing by default.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="endpoint">The configuration section of the endpoint (its key is the endpoint name).</param>
    /// <param name="configuration">
    /// The section of this option in the endpoint section. Null when the option is enabled by default or by <c>true</c>.
    /// </param>
    /// <returns>True on success, false on error (errors must be logged).</returns>
    public virtual bool Initialize( IActivityMonitor monitor, ImmutableConfigurationSection endpoint, ImmutableConfigurationSection? configuration ) => true;

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
