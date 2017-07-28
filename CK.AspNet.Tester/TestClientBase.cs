using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace CK.AspNet.Tester
{
    /// <summary>
    /// Generalization of <see cref="TestClient"/> and <see cref="TestServerClient"/>.
    /// </summary>
    public abstract class TestClientBase : IDisposable
    {
        int _maxAutomaticRedirections;

        /// <summary>
        /// Initializes a new <see cref="TestClientBase"/>.
        /// </summary>
        /// <param name="baseAddress">The base address. Can be null (relative urls are not supported).</param>
        /// <param name="cookies">The cookie container.</param>
        protected TestClientBase( Uri baseAddress, CookieContainer cookies )
        {
            AuthorizationHeaderName = "Authorization";
            BaseAddress = baseAddress;
            Cookies = cookies;
            MaxAutomaticRedirections = 50;
        }

        /// <summary>
        /// Gets or sets the authorization header (defaults to "Authorization").
        /// When <see cref="Token"/> is set to a non null token, requests have 
        /// the 'AuthorizationHeaderName Bearer token" added to any requests
        /// to url on <see cref="BaseAddress"/>.
        /// </summary>
        public string AuthorizationHeaderName { get; set; }

        /// <summary>
        /// Gets the base address.
        /// </summary>
        public Uri BaseAddress { get; }

        /// <summary>
        /// Gets the <see cref="CookieContainer"/>.
        /// </summary>
        public CookieContainer Cookies { get; }

        /// <summary>
        /// Clears cookies from a base path and optional sub paths.
        /// </summary>
        /// <param name="basePath">The base url. Should not be null.</param>
        /// <param name="subPath">Sub paths for which cookies must be cleared.</param>
        public void ClearCookies( Uri basePath, IEnumerable<string> subPath )
        {
            foreach( Cookie c in Cookies.GetCookies( basePath ) )
            {
                c.Expired = true;
            }
            if( subPath != null )
            {
                foreach( string u in subPath )
                {
                    if( string.IsNullOrWhiteSpace( u ) ) continue;
                    Uri normalized = new Uri( basePath, u[u.Length - 1] != '/' ? u + '/' : u );
                    foreach( Cookie c in Cookies.GetCookies( normalized ) )
                    {
                        c.Expired = true;
                    }
                }
            }
        }

        /// <summary>
        /// Clears cookies from a base path and optional sub paths.
        /// </summary>
        /// <param name="basePath">The base url. Should not be null.</param>
        /// <param name="subPath">Optional sub paths for which cookies must be cleared.</param>
        public void ClearCookies( Uri basePath, params string[] subPath ) => ClearCookies( basePath, (IEnumerable<string>)subPath );

        /// <summary>
        /// Clears cookies from <see cref="BaseAddress"/> and optional sub paths.
        /// </summary>
        public void ClearCookies( params string[] subPath ) => ClearCookies( BaseAddress, subPath );

        /// <summary>
        /// Gets or sets the maximum number of redirections that will be automatically followed.
        /// Defaults to 50.
        /// Set it to 0 to manually follow redirections thanks to <see cref="FollowRedirect(HttpResponseMessage, bool)"/>.
        /// </summary>
        public int MaxAutomaticRedirections
        {
            get => _maxAutomaticRedirections;
            set => _maxAutomaticRedirections = value <= 0 ? 0 : value;
        }

        /// <summary>
        /// Gets or sets the authorization token or clears it (by setting it to null).
        /// This token must be sent only to urls on <see cref="BaseAddress"/>.
        /// </summary>
        public abstract string Token { get; set; }

        /// <summary>
        /// Follows a redirected url once if the response's status is <see cref="HttpStatusCode.Moved"/> (301) 
        /// or <see cref="HttpStatusCode.Found"/> (302).
        /// </summary>
        /// <param name="response">The initial response.</param>
        /// <param name="throwIfNotRedirect">
        /// When the <paramref name="response"/> is not a 301 or 302 and this is true, this method 
        /// throws an exception. When this parameter is false, the <paramref name="response"/>
        /// is returned (since it is the final redirected response).</param>
        /// <returns>The redirected response.</returns>
        /// <remarks>
        /// This should be used with a small or 0 <see cref="MaxAutomaticRedirections"/> value since
        /// otherwise redirections are automatically followed.
        /// A redirection always uses the GET method.
        /// </remarks>
        public HttpResponseMessage FollowRedirect( HttpResponseMessage response, bool throwIfNotRedirect = false )
        {
            if( response.StatusCode != HttpStatusCode.Moved && response.StatusCode != HttpStatusCode.Found )
            {
                if( throwIfNotRedirect ) throw new Exception( "Response must be a 301 Moved or a 302 Found." );
                return response;
            }
            var redirectUrl = response.Headers.Location;
            if( !redirectUrl.IsAbsoluteUri )
            {
                redirectUrl = new Uri( response.RequestMessage.RequestUri, redirectUrl );
            }
            return Get( redirectUrl );
        }

        /// <summary>
        /// Issues a GET request to the relative url on <see cref="BaseAddress"/> or to an absolute url.
        /// </summary>
        /// <param name="url">The BaseAddress relative url or an absolute url.</param>
        /// <returns>The response.</returns>
        public HttpResponseMessage Get( string url )
        {
            return Get( new Uri( url, UriKind.RelativeOrAbsolute ) );
        }

        /// <summary>
        /// Issues a GET request to the relative url on <see cref="BaseAddress"/> or to an absolute url.
        /// </summary>
        /// <param name="url">The BaseAddress relative url or an absolute url.</param>
        /// <returns>The response.</returns>
        public virtual HttpResponseMessage Get( Uri url ) => AutoFollowRedirect( DoGet( url ) );

        /// <summary>
        /// Issues a GET request to the relative url on <see cref="BaseAddress"/> or to an absolute url.
        /// Implementations must handle <see cref="Token"/>, <see cref="Cookies"/> (but not redirections).
        /// </summary>
        /// <param name="url">The BaseAddress relative url or an absolute url.</param>
        /// <returns>The response.</returns>
        internal protected abstract HttpResponseMessage DoGet( Uri url );

        /// <summary>
        /// Issues a POST request to the relative url on <see cref="BaseAddress"/> or to an absolute url
        /// with form values.
        /// </summary>
        /// <param name="url">The BaseAddress relative url or an absolute url.</param>
        /// <param name="formValues">The form values.</param>
        /// <returns>The response.</returns>
        public HttpResponseMessage Post( string url, IEnumerable<KeyValuePair<string, string>> formValues )
        {
            return Post( new Uri( url, UriKind.RelativeOrAbsolute ), formValues );
        }

        /// <summary>
        /// Issues a POST request to the relative url on <see cref="BaseAddress"/> or to an absolute url 
        /// with an "application/json" content.
        /// </summary>
        /// <param name="url">The BaseAddress relative url or an absolute url.</param>
        /// <param name="json">The json content.</param>
        /// <returns>The response.</returns>
        public HttpResponseMessage PostJSON( string url, string json ) => PostJSON( new Uri( url, UriKind.RelativeOrAbsolute ), json );

        /// <summary>
        /// Issues a POST request to the relative url on <see cref="BaseAddress"/> or to an absolute url 
        /// with an "application/json; charset=utf-8" content.
        /// </summary>
        /// <param name="url">The BaseAddress relative url or an absolute url.</param>
        /// <param name="json">The json content.</param>
        /// <returns>The response.</returns>
        public HttpResponseMessage PostJSON( Uri url, string json )
        {
            var c = new StringContent( json, Encoding.UTF8, "application/json" );
            return Post( url, c );
        }

        /// <summary>
        /// Issues a POST request to the relative url on <see cref="BaseAddress"/> or to an absolute url 
        /// with an "application/xml; charset=utf-8" content.
        /// </summary>
        /// <param name="url">The BaseAddress relative url or an absolute url.</param>
        /// <param name="xml">The xml content.</param>
        /// <returns>The response.</returns>
        public HttpResponseMessage PostXml( string url, string xml ) => PostXml( new Uri( url, UriKind.RelativeOrAbsolute ), xml );


        /// <summary>
        /// Issues a POST request to the relative url on <see cref="BaseAddress"/> or to an absolute url 
        /// with an "application/xml; charset=utf-8" content.
        /// </summary>
        /// <param name="url">The BaseAddress relative url or an absolute url.</param>
        /// <param name="xml">The xml content.</param>
        /// <returns>The response.</returns>
        public HttpResponseMessage PostXml( Uri url, string xml )
        {
            var c = new StringContent( xml, Encoding.UTF8, "application/xml" );
            return Post( url, c );
        }

        /// <summary>
        /// Issues a POST request to the relative url on <see cref="BaseAddress"/> or to an absolute url
        /// with form values.
        /// </summary>
        /// <param name="url">The BaseAddress relative url or an absolute url.</param>
        /// <param name="formValues">The form values (compatible with a IDictionary&lt;string, string&gt;).</param>
        /// <returns>The response.</returns>
        public HttpResponseMessage Post( Uri url, IEnumerable<KeyValuePair<string, string>> formValues )
        {
            return Post( url, new FormUrlEncodedContent( formValues ) );
        }

        /// <summary>
        /// Issues a POST request to the relative url on <see cref="BaseAddress"/> or to an absolute url 
        /// with an <see cref="HttpContent"/>.
        /// </summary>
        /// <param name="url">The BaseAddress relative url or an absolute url.</param>
        /// <param name="content">The content.</param>
        /// <returns>The response.</returns>
        public virtual HttpResponseMessage Post( Uri url, HttpContent content ) => AutoFollowRedirect( DoPost( url, content ) );

        /// <summary>
        /// Issues a POST request to the relative url on <see cref="BaseAddress"/> or to an absolute url 
        /// with an <see cref="HttpContent"/>.
        /// Implementations must handle <see cref="Token"/>, <see cref="Cookies"/> (but not the redirections).
        /// </summary>
        /// <param name="url">The BaseAddress relative url or an absolute url.</param>
        /// <param name="content">The content.</param>
        /// <returns>The response.</returns>
        internal protected abstract HttpResponseMessage DoPost( Uri url, HttpContent content );

        /// <summary>
        /// Follows at most <see cref="MaxAutomaticRedirections"/>.
        /// </summary>
        /// <param name="m">The original response.</param>
        /// <returns>The final response.</returns>
        protected HttpResponseMessage AutoFollowRedirect( HttpResponseMessage m )
        {
            int redirection = _maxAutomaticRedirections;
            while( --redirection >= 0 )
            {
                var next = FollowRedirect( m, throwIfNotRedirect: false );
                if( next == m ) break;
                m = next;
            }
            return m;
        }

        /// <summary>
        /// Must dispose any resources specific to this client.
        /// </summary>
        public abstract void Dispose();
    }
}