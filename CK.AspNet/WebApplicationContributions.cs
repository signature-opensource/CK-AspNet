using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.AspNet;

/// <summary>
/// The contributions of the packages to the web applications of a host: one instance per host, in the host's services.
/// <para>
/// A package registers its contribution when the map is registered, from the <c>ConfigureServices</c> method of one of
/// its real objects (a real object is shared by the hosts that load the same map: this is where the per-host state is):
/// <code>
/// void ConfigureServices( StObjContextRoot.ServiceRegister services )
/// {
///     WebApplicationContributions.FindOrAdd( services.Services ).Add( web => web.EnableClients() );
/// }
/// </code>
/// A contribution configures a <see cref="WebApplicationBuilder"/> in one gesture: its services and its middlewares
/// (<see cref="ApplicationBuilderCKAspNetExtensions.AppendApplicationBuilder"/>,
/// <see cref="ApplicationBuilderCKAspNetExtensions.PrependApplicationBuilder"/>), by convention through an idempotent
/// "Enable" extension method (see <see cref="ApplicationBuilderCKAspNetExtensions.TryEnable"/>).
/// </para>
/// <para>
/// <c>CKBuild</c> doesn't apply them: the host that creates the web applications does (the "HttpEndpoint" feature of
/// CK.AppIdentity.HttpEndpoint applies them to its endpoint).
/// </para>
/// </summary>
public sealed class WebApplicationContributions
{
    readonly List<Action<WebApplicationBuilder>> _contributions;

    WebApplicationContributions()
    {
        _contributions = new List<Action<WebApplicationBuilder>>();
    }

    /// <summary>
    /// Gets the contributions, in their registration order (the real objects' dependency order).
    /// </summary>
    public IReadOnlyList<Action<WebApplicationBuilder>> Contributions => _contributions;

    /// <summary>
    /// Adds a contribution.
    /// </summary>
    /// <param name="contribution">The contribution to add.</param>
    public void Add( Action<WebApplicationBuilder> contribution )
    {
        Throw.CheckNotNullArgument( contribution );
        _contributions.Add( contribution );
    }

    /// <summary>
    /// Applies the contributions to a builder.
    /// </summary>
    /// <param name="builder">The builder of a web application.</param>
    public void ApplyTo( WebApplicationBuilder builder )
    {
        Throw.CheckNotNullArgument( builder );
        foreach( var c in _contributions ) c( builder );
    }

    /// <summary>
    /// Finds the instance registered in the <paramref name="services"/> or registers a new one.
    /// </summary>
    /// <param name="services">The services of the host.</param>
    /// <returns>The contributions of the host.</returns>
    public static WebApplicationContributions FindOrAdd( IServiceCollection services )
    {
        Throw.CheckNotNullArgument( services );
        var d = services.FirstOrDefault( d => d.ServiceType == typeof( WebApplicationContributions ) && !d.IsKeyedService && d.ImplementationInstance != null );
        if( d != null ) return (WebApplicationContributions)d.ImplementationInstance!;
        var c = new WebApplicationContributions();
        services.AddSingleton( c );
        return c;
    }
}
