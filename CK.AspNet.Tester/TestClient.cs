using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CK.AspNet.Tester
{
    /// <summary>
    /// Client helper that wraps an <see cref="HttpClient"/> and provides simple methods (synchronous)
    /// to easily Get/Post requests, manage cookies and a token, follow redirects (or not) and Reads the response contents.
    /// </summary>
    public class TestClient : TestClientBase
    {
        readonly HttpClient _httpClient;
        string _token;

        /// <summary>
        /// Initializes a new client.
        /// </summary>
        /// <param name="baseAdress">Absolute url.</param>
        public TestClient( string baseAdress )
            : base( new Uri( baseAdress, UriKind.Absolute ), new CookieContainer() )
        {
            _httpClient = new HttpClient( new HttpClientHandler()
            {
                CookieContainer = Cookies,
                AllowAutoRedirect = false
            } );
            OnReceiveMessage = DefaultOnReceiveMessage;
        }

        /// <summary>
        /// Gets or sets the authorization token or clears it (by setting it to null).
        /// This token will be sent only to urls on BaseAddress.
        /// </summary>
        public override string Token
        {
            get => _token;
            set
            {
                if( _token != value )
                {
                    if( _token != null ) _httpClient.DefaultRequestHeaders.Remove( AuthorizationHeaderName );
                    _token = value;
                    if( _token != null ) _httpClient.DefaultRequestHeaders.Add( AuthorizationHeaderName, "Bearer " + _token );
                }
            }
        }

        /// <summary>
        /// Issues a GET request to the relative url on <see cref="TestClientBase.BaseAddress"/> or to an absolute url.
        /// </summary>
        /// <param name="url">The BaseAddress relative url or an absolute url.</param>
        /// <returns>The response.</returns>
        internal async protected override Task<HttpResponseMessage> DoGet( Uri url )
        {
            var absoluteUrl = new Uri( BaseAddress, url );
            string currentToken = _token;
            if( currentToken != null && !BaseAddress.IsBaseOf( url ) ) Token = null;
            var r = await _httpClient.GetAsync( absoluteUrl );
            Token = currentToken;
            return r;
        }

        /// <summary>
        /// Default <see cref="TestClientBase.OnReceiveMessage"/> implementation.
        /// Calls <see cref="TestClientBase.UpdateCookiesWithPathHandling"/> and
        /// returns true to follow redirects.
        /// </summary>
        /// <param name="m">The received message.</param>
        /// <returns>True to auto follow redirects if any.</returns>
        public virtual Task<bool> DefaultOnReceiveMessage( HttpResponseMessage m )
        {
            UpdateCookiesWithPathHandling( Cookies, m );
            return Task.FromResult( true );
        }

        /// <summary>
        /// Issues a POST request to the relative url on <see cref="TestClientBase.BaseAddress"/> or to an absolute url 
        /// with an <see cref="HttpContent"/>.
        /// </summary>
        /// <param name="url">The BaseAddress relative url or an absolute url.</param>
        /// <param name="content">The content.</param>
        /// <returns>The response.</returns>
        internal protected async override Task<HttpResponseMessage> DoPost( Uri url, HttpContent content )
        {
            var absoluteUrl = new Uri( BaseAddress, url );
            string currentToken = _token;
            if( currentToken != null && !BaseAddress.IsBaseOf( url ) ) Token = null;
            HttpResponseMessage r = await _httpClient.PostAsync( absoluteUrl, content );
            Token = currentToken;
            return r;
        }

        /// <summary>
        /// Dispose the inner <see cref="HttpClient"/>.
        /// </summary>
        public override void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}


