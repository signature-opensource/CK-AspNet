using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Net.Http.Headers;
using System.Threading.Tasks;

namespace CK.AspNet.Tester
{
    /// <summary>
    /// Client helper that wraps a <see cref="TestServer"/> and provides simple methods (synchronous)
    /// to easily Get/Post requests, manage cookies and a token, follow redirects (or not) and Reads the response contents.
    /// </summary>
    public class TestServerClient : TestClientBase
    {
        readonly TestServer _testServer;
        HttpClient _externalClient;
        readonly bool _disposeTestServer;

        /// <summary>
        /// Initializes a new client for a <see cref="TestServer"/>.
        /// </summary>
        /// <param name="testServer">The test server.</param>
        /// <param name="disposeTestServer">False to leave the TestServer alive when disposing this client.</param>
        public TestServerClient( TestServer testServer, bool disposeTestServer = true )
            : base( testServer.BaseAddress, new CookieContainer() )
        {
            _testServer = testServer;
            _disposeTestServer = disposeTestServer;
        }

        /// <summary>
        /// Gets a direct access to the <see cref="TestServer"/>.
        /// </summary>
        public TestServer Server => _testServer;

        /// <summary>
        /// Gets or sets the authorization token or clears it (by setting it to null).
        /// </summary>
        public override string Token { get; set; }

        /// <summary>
        /// Issues a GET request to the relative url on <see cref="TestClientBase.BaseAddress"/> or to an absolute url.
        /// </summary>
        /// <param name="url">The BaseAddress relative url or an absolute url.</param>
        /// <returns>The response.</returns>
        internal async protected override Task<HttpResponseMessage> DoGet( Uri url )
        {
            if( url.IsAbsoluteUri && !BaseAddress.IsBaseOf( url ) ) 
            {
                return await GetExternalClient().GetAsync( url );
            }
            var absoluteUrl = new Uri( _testServer.BaseAddress, url );
            var requestBuilder = _testServer.CreateRequest( absoluteUrl.ToString() );
            AddCookies( requestBuilder, absoluteUrl );
            AddToken( requestBuilder );
            var response = await requestBuilder.GetAsync();
            UpdateCookies( response, absoluteUrl );
            return response;
        }

        /// <summary>
        /// Issues a POST request to the relative url on <see cref="TestServer.BaseAddress"/> with an <see cref="HttpContent"/>.
        /// </summary>
        /// <param name="url">The relative or absolute url.</param>
        /// <param name="content">The content.</param>
        /// <returns>The response.</returns>
        internal async protected override Task<HttpResponseMessage> DoPost( Uri url, HttpContent content )
        {
            if( url.IsAbsoluteUri && !BaseAddress.IsBaseOf( url ) )
            {
                return await GetExternalClient().PostAsync( url, content );
            }
            var absoluteUrl = new Uri( _testServer.BaseAddress, url );
            var requestBuilder = _testServer.CreateRequest( absoluteUrl.ToString() );
            AddCookies( requestBuilder, absoluteUrl );
            AddToken( requestBuilder );
            var response = await requestBuilder.And( message =>
             {
                 message.Content = content;
             } ).PostAsync();
            UpdateCookies( response, absoluteUrl );
            return response;
        }

        /// <summary>
        /// Dispose the inner <see cref="TestServer"/>.
        /// </summary>
        public override void Dispose()
        {
            if( _externalClient != null )
            {
                _externalClient.Dispose();
                _externalClient = null;
            }
            if( _disposeTestServer ) _testServer.Dispose();
        }

        HttpClient GetExternalClient()
        {
            if( _externalClient == null )
            {
                _externalClient = new HttpClient( new HttpClientHandler()
                {
                    CookieContainer = Cookies,
                    AllowAutoRedirect = false
                } );
            }
            return _externalClient;
        }

        void AddToken( RequestBuilder requestBuilder )
        {
            if( Token != null )
            {
                requestBuilder.AddHeader( AuthorizationHeaderName, "Bearer " + Token );
            }
        }

        void AddCookies( RequestBuilder requestBuilder, Uri absoluteUrl )
        {
            var cookieHeader = Cookies.GetCookieHeader( absoluteUrl );
            if( !string.IsNullOrWhiteSpace( cookieHeader ) )
            {
                requestBuilder.AddHeader( HeaderNames.Cookie, cookieHeader );
            }
        }

        void UpdateCookies( HttpResponseMessage response, Uri absoluteUrl )
        {
            if( response.Headers.Contains( HeaderNames.SetCookie ) )
            {
                var cookies = response.Headers.GetValues( HeaderNames.SetCookie );
                foreach( var cookie in cookies )
                {
                    Cookies.SetCookies( absoluteUrl, cookie );
                }
            }
        }
    }
}

