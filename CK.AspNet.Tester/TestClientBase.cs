using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CK.AspNet.Tester
{
    /// <summary>
    /// Generalization of <see cref="TestClient"/> and <see cref="TestServerClient"/>.
    /// This offers a common API to test against a <see cref="Microsoft.AspNetCore.TestHost.TestServer"/>
    /// as well as a real, external, server.
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
        /// Response message of <see cref="DoGet"/> and <see cref="DoPost"/> must be handled
        /// by <see cref="UpdateCookies"/> to fix the cookie container Path bug.
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
        /// Issues a GET request to the relative url on <see cref="BaseAddress"/> or to an absolute url.
        /// </summary>
        /// <param name="url">The BaseAddress relative url or an absolute url.</param>
        /// <returns>The response.</returns>
        public Task<HttpResponseMessage> Get( string url )
        {
            return Get( new Uri( url, UriKind.RelativeOrAbsolute ) );
        }

        /// <summary>
        /// Issues a GET request to the relative url on <see cref="BaseAddress"/> or to an absolute url.
        /// </summary>
        /// <param name="url">The BaseAddress relative url or an absolute url.</param>
        /// <returns>The response.</returns>
        async public virtual Task<HttpResponseMessage> Get( Uri url ) => await AutoFollowRedirect( await DoGet( url ) );

        /// <summary>
        /// Issues a GET request to the relative url on <see cref="BaseAddress"/> or to an absolute url.
        /// Implementations must handle <see cref="Token"/>, <see cref="Cookies"/> (thanks
        /// to <see cref="UpdateCookiesSimple"/> or <see cref="UpdateCookiesWithPathHandling"/> helpers
        /// for instance), but not the redirections.
        /// </summary>
        /// <param name="url">The BaseAddress relative url or an absolute url.</param>
        /// <returns>The response.</returns>
        internal protected abstract Task<HttpResponseMessage> DoGet( Uri url );

        /// <summary>
        /// Issues a POST request to the relative url on <see cref="BaseAddress"/> or to an absolute url
        /// with form values.
        /// </summary>
        /// <param name="url">The BaseAddress relative url or an absolute url.</param>
        /// <param name="formValues">The form values.</param>
        /// <returns>The response.</returns>
        public Task<HttpResponseMessage> Post( string url, IEnumerable<KeyValuePair<string, string>> formValues )
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
        public Task<HttpResponseMessage> PostJSON( string url, string json ) => PostJSON( new Uri( url, UriKind.RelativeOrAbsolute ), json );

        /// <summary>
        /// Issues a POST request to the relative url on <see cref="BaseAddress"/> or to an absolute url 
        /// with an "application/json; charset=utf-8" content.
        /// </summary>
        /// <param name="url">The BaseAddress relative url or an absolute url.</param>
        /// <param name="json">The json content.</param>
        /// <returns>The response.</returns>
        public Task<HttpResponseMessage> PostJSON( Uri url, string json )
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
        public Task<HttpResponseMessage> PostXml( string url, string xml ) => PostXml( new Uri( url, UriKind.RelativeOrAbsolute ), xml );


        /// <summary>
        /// Issues a POST request to the relative url on <see cref="BaseAddress"/> or to an absolute url 
        /// with an "application/xml; charset=utf-8" content.
        /// </summary>
        /// <param name="url">The BaseAddress relative url or an absolute url.</param>
        /// <param name="xml">The xml content.</param>
        /// <returns>The response.</returns>
        public Task<HttpResponseMessage> PostXml( Uri url, string xml )
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
        public Task<HttpResponseMessage> Post( Uri url, IEnumerable<KeyValuePair<string, string>> formValues )
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
        public async virtual Task<HttpResponseMessage> Post( Uri url, HttpContent content ) => await AutoFollowRedirect( await DoPost( url, content ) );

        /// <summary>
        /// Issues a POST request to the relative url on <see cref="BaseAddress"/> or to an absolute url 
        /// with an <see cref="HttpContent"/>.
        /// Implementations must handle <see cref="Token"/>, <see cref="Cookies"/> (thanks
        /// to <see cref="UpdateCookiesSimple"/> or <see cref="UpdateCookiesWithPathHandling"/> helpers
        /// for instance), but not the redirections.
        /// </summary>
        /// <param name="url">The BaseAddress relative url or an absolute url.</param>
        /// <param name="content">The content.</param>
        /// <returns>The response.</returns>
        internal protected abstract Task<HttpResponseMessage> DoPost( Uri url, HttpContent content );

        /// <summary>
        /// Follows at most <see cref="MaxAutomaticRedirections"/>.
        /// </summary>
        /// <param name="m">The original response.</param>
        /// <returns>The final response.</returns>
        protected async Task<HttpResponseMessage> AutoFollowRedirect( HttpResponseMessage m )
        {
            int redirection = _maxAutomaticRedirections;
            while( --redirection >= 0 )
            {
                var next = await FollowRedirect( m, throwIfNotRedirect: false );
                if( next == m ) break;
                m = next;
            }
            return m;
        }

        /// <summary>
        /// Gets or sets a <see cref="HttpResponseMessage"/> handler.
        /// This handler will be called immediatly after the <see cref="DoPost"/> or <see cref="DoGet"/>
        /// methods and is typically in charge of handling cookies.
        /// This handler must return true to automatically call <see cref="AutoFollowRedirect"/>
        /// or false if for any reason, AutoFollowRedirect must not be done.
        /// This property MUST not be null.
        /// </summary>
        public Func<HttpResponseMessage,Task<bool>> OnReceiveMessage { get; set; }

        async Task<HttpResponseMessage> HandleResponse( HttpResponseMessage m )
        {
            if( OnReceiveMessage == null ) throw new InvalidOperationException( $"{nameof(OnReceiveMessage)} must not be null." );
            return await OnReceiveMessage( m )
                    ? await AutoFollowRedirect( m )
                    : m;
        }

        /// <summary>
        /// Follows a redirected url once if the response's status is <see cref="HttpStatusCode.Moved"/> (301), 
        /// <see cref="HttpStatusCode.Found"/> (302) or <see cref="HttpStatusCode.SeeOther"/> (303).
        /// The <see cref="HttpStatusCode.RedirectMethod"/> (303) will raise a <see cref="NotSupportedException"/>
        /// at this time.
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
        public virtual Task<HttpResponseMessage> FollowRedirect( HttpResponseMessage response, bool throwIfNotRedirect = false )
        {
            if( response.StatusCode == HttpStatusCode.TemporaryRedirect )
            {
                throw new NotSupportedException( "307 TemporaryRedirect is not supported." );
            }
            if( response.StatusCode != HttpStatusCode.Moved
                && response.StatusCode != HttpStatusCode.Found
                && response.StatusCode != HttpStatusCode.SeeOther )
            {
                if( throwIfNotRedirect ) throw new Exception( "Response must be a 301 Moved, 302 Found or 303 See Other." );
                return Task.FromResult( response );
            }
            var redirectUrl = response.Headers.Location;
            if( !redirectUrl.IsAbsoluteUri )
            {
                redirectUrl = new Uri( response.RequestMessage.RequestUri, redirectUrl );
            }
            response.Dispose();
            return DoGet( redirectUrl );
        }


        /// <summary>
        /// Must dispose any resources specific to this client.
        /// </summary>
        public abstract void Dispose();

        static readonly Regex _rCookiePath = new Regex( "(?<=^|;)\\s*path\\s*=\\s*(?<p>.*?);?", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture );

        /// <summary>
        /// Corrects CookieContainer behavior.
        /// See: https://github.com/dotnet/corefx/issues/21250#issuecomment-309613552
        /// This fix the Cookie path bug of the CookieContainer but does not handle any other
        /// specification from current (since 2011) https://tools.ietf.org/html/rfc6265.
        /// </summary>
        /// <param name="container">The cookie container to update.</param>
        /// <param name="response">The response message obtained from <see cref="DoGet"/> or <see cref="DoPost"/>.</param>
        static public void UpdateCookiesWithPathHandling( CookieContainer container, HttpResponseMessage response )
        {
            var absoluteUrl = GetCheckedRequestAbsoluteUri( container, response );
            if( response.Headers.Contains( HeaderNames.SetCookie ) )
            {
                var root = new Uri( absoluteUrl.GetLeftPart( UriPartial.Authority ) );
                var cookies = response.Headers.GetValues( HeaderNames.SetCookie );
                foreach( var cookie in cookies )
                {
                    string cFinal;
                    Uri rFinal;
                    Match m = _rCookiePath.Match( cookie );
                    if( m.Success )
                    {
                        // Last Path wins: see https://tools.ietf.org/html/rfc6265#section-5.3 §7.
                        do
                        {
                            cFinal = cookie.Remove( m.Index, m.Length );
                            rFinal = new Uri( root, m.Groups[1].Value );
                            m = m.NextMatch();
                        }
                        while( m.Success );
                    }
                    else
                    {
                        cFinal = cookie;
                        rFinal = root;
                    }
                    container.SetCookies( rFinal, cFinal );
                }
            }
        }

        /// <summary>
        /// Update cookie container by simply handling Set-Cookie response headers.
        /// </summary>
        /// <param name="container">The cookie container to update.</param>
        /// <param name="response">The response message obtained from <see cref="DoGet"/> or <see cref="DoPost"/>.</param>
        static public void UpdateCookiesSimple( CookieContainer container, HttpResponseMessage response )
        {
            var absoluteUrl = GetCheckedRequestAbsoluteUri( container, response );
            if( response.Headers.Contains( HeaderNames.SetCookie ) )
            {
                var cookies = response.Headers.GetValues( HeaderNames.SetCookie );
                foreach( var cookie in cookies )
                {
                    container.SetCookies( absoluteUrl, cookie );
                }
            }
        }

        /// <summary>
        /// Helper that checks its parameters and returns the request uri.
        /// </summary>
        /// <param name="container">A cookie container that must not be null./</param>
        /// <param name="response">The received response.</param>
        /// <returns>The requested uri.</returns>
        public static Uri GetCheckedRequestAbsoluteUri( CookieContainer container, HttpResponseMessage response )
        {
            if( container == null ) throw new ArgumentNullException( nameof( container ) );
            if( response == null ) throw new ArgumentNullException( nameof( response ) );
            var uri = response.RequestMessage.RequestUri;
            if( uri == null ) throw new ArgumentNullException( "response.RequestMessage.RequestUri" );
            if( !uri.IsAbsoluteUri ) throw new ArgumentException( "Uri must be absolute.", "response.RequestMessage.RequestUri" );
            return uri;
        }

    }
}
