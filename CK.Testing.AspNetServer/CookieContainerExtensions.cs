using CK.Core;
using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace CK.Testing;

/// <summary>
/// Brings useful extensions to cookie container.
/// </summary>
public static class CookieContainerExtensions
{
    /// <summary>
    /// Clears cookies from a base path and optional sub paths.
    /// </summary>
    /// <param name="container">The cookie container to update.</param>
    /// <param name="basePath">The base url. Should not be null.</param>
    /// <param name="subPath">Sub paths for which cookies must be cleared.</param>
    static public void ClearCookies( this CookieContainer container, Uri basePath, IEnumerable<string> subPath )
    {
        foreach( Cookie c in container.GetCookies( basePath ) )
        {
            c.Expired = true;
        }
        if( subPath != null )
        {
            foreach( string u in subPath )
            {
                if( string.IsNullOrWhiteSpace( u ) ) continue;
                Uri normalized = new Uri( basePath, u[^1] != '/' ? u + '/' : u );
                foreach( Cookie c in container.GetCookies( normalized ) )
                {
                    c.Expired = true;
                }
            }
        }
    }

    /// <summary>
    /// Clears cookies from a base path and optional sub paths.
    /// </summary>
    /// <param name="container">The cookie container to update.</param>
    /// <param name="basePath">The base url. Should not be null.</param>
    /// <param name="subPath">Optional sub paths for which cookies must be cleared.</param>
    static public void ClearCookies( this CookieContainer container, Uri basePath, params string[] subPath ) => ClearCookies( container, basePath, (IEnumerable<string>)subPath );

}
