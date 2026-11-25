using CK.Core;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.AppIdentity;

/// <summary>
/// The configuration of the "HttpEndpoint" feature of the root party ("Local:HttpEndpoint"): either its <see cref="Urls"/>
/// or a <see cref="Kestrel"/> section, unless an option provides the server of the endpoint
/// (<see cref="HttpEndpointOption.ServerUrl"/>). The other keys of the section are its options.
/// </summary>
[ConfigurationModel]
public sealed class HttpEndpointConfiguration : IConfigurationModel
{
    /// <summary>
    /// Gets the URLs of the endpoint (each one may hold several URLs separated by ';', like ASP.NET's "urls"): the first one
    /// is the default URL of the endpoint. The URLs are the ones of a deployment: each one writes its own.
    /// </summary>
    public IReadOnlyList<string>? Urls { get; init; }

    /// <summary>
    /// Gets Kestrel's own configuration of the endpoint (its Endpoints, Certificates, Limits...), instead of the
    /// <see cref="Urls"/>.
    /// </summary>
    [ConfigurationSchema( "https://json.schemastore.org/appsettings.json#/definitions/kestrel" )]
    public IConfiguration? Kestrel { get; init; }

    /// <summary>
    /// Gets the name of the Kestrel endpoint ("Kestrel:Endpoints:{name}") whose "Url" is the default URL of the endpoint
    /// (with <see cref="Urls"/>, the first one is the default one).
    /// </summary>
    public string? DefaultEndpoint { get; init; }

    /// <summary>
    /// Gets the URLs, the ones separated by ';' split, or null when there is none.
    /// </summary>
    public string[]? EffectiveUrls
    {
        get
        {
            var urls = Urls?.SelectMany( u => u.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) ).ToArray();
            return urls is { Length: > 0 } ? urls : null;
        }
    }

    /// <inheritdoc />
    public void Validate( IConfigurationValidationContext context )
    {
        if( EffectiveUrls != null && Kestrel != null )
        {
            context.Error( $"'{context.Path}' has \"Urls\" and a \"Kestrel\" section: one of them is expected." );
        }
        if( !string.IsNullOrWhiteSpace( DefaultEndpoint ) )
        {
            if( Kestrel == null )
            {
                context.Error( $"'{context.Path}:DefaultEndpoint' is only for a \"Kestrel\" section: with \"Urls\", the first one is the default one." );
            }
            else if( string.IsNullOrWhiteSpace( Kestrel[$"Endpoints:{DefaultEndpoint}:Url"] ) )
            {
                context.Error( $"'{context.Path}:DefaultEndpoint' is '{DefaultEndpoint}': there is no \"{context.Path}:Kestrel:Endpoints:{DefaultEndpoint}:Url\"." );
            }
        }
    }
}
