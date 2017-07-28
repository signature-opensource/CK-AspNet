using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Net.Http.Headers;

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

        /// <summary>
        /// Initializes a new client for a <see cref="TestServer"/>.
        /// </summary>
        /// <param name="testServer">The test server.</param>
        public TestServerClient( TestServer testServer )
            : base( testServer.BaseAddress, new CookieContainer() )
        {
            _testServer = testServer;
        }

        /// <summary>
        /// Gets or sets the authorization token or clears it (by setting it to null).
        /// </summary>
        public override string Token { get; set; }

        /// <summary>
        /// Issues a GET request to the relative url on <see cref="TestClientBase.BaseAddress"/> or to an absolute url.
        /// </summary>
        /// <param name="url">The BaseAddress relative url or an absolute url.</param>
        /// <returns>The response.</returns>
        internal protected override HttpResponseMessage DoGet( Uri url )
        {
            if( url.IsAbsoluteUri && !BaseAddress.IsBaseOf( url ) ) 
            {
                return GetExternalClient().GetAsync( url ).Result;
            }
            var absoluteUrl = new Uri( _testServer.BaseAddress, url );
            var requestBuilder = _testServer.CreateRequest( absoluteUrl.ToString() );
            AddCookies( requestBuilder, absoluteUrl );
            AddToken( requestBuilder );
            var response = requestBuilder.GetAsync().Result;
            UpdateCookies( response, absoluteUrl );
            return response;
        }

        /// <summary>
        /// Issues a POST request to the relative url on <see cref="TestServer.BaseAddress"/> with an <see cref="HttpContent"/>.
        /// </summary>
        /// <param name="url">The relative or absolute url.</param>
        /// <param name="content">The content.</param>
        /// <returns>The response.</returns>
        internal protected override HttpResponseMessage DoPost( Uri url, HttpContent content )
        {
            if( url.IsAbsoluteUri && !BaseAddress.IsBaseOf( url ) )
            {
                return GetExternalClient().PostAsync( url, content ).Result;
            }
            var absoluteUrl = new Uri( _testServer.BaseAddress, url );
            var requestBuilder = _testServer.CreateRequest( absoluteUrl.ToString() );
            AddCookies( requestBuilder, absoluteUrl );
            AddToken( requestBuilder );
            var response = requestBuilder.And( message =>
             {
                 message.Content = content;
             } ).PostAsync().Result;
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
            _testServer.Dispose();
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

