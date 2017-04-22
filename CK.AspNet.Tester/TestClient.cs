using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Net.Http.Headers;

namespace CK.AspNet.Tester
{
    /// <summary>
    /// Client helper.
    /// </summary>
    public class TestClient
    {
        readonly TestServer _testServer;
        string _token;

        /// <summary>
        /// Initializes a new client for a <see cref="TestServer"/>.
        /// </summary>
        /// <param name="testServer">The test server.</param>
        public TestClient(TestServer testServer)
        {
            if (testServer == null) throw new ArgumentNullException(nameof(Tester));
            _testServer = testServer;
            Cookies = new CookieContainer();
        }

        /// <summary>
        /// Gets or sets the authorization header (defaults to "Authorization").
        /// When <see cref="SetToken"/> is called with a non null token, 
        /// requests have the 'AuthorizationHeaderName Bearer token" added.
        /// </summary>
        public string AuthorizationHeaderName { get; set; } = "Authorization";

        /// <summary>
        /// Sets the authorization token or clears it (by setting it to null).
        /// </summary>
        /// <param name="token">The authorization token that will be sent with each request.</param>
        public void SetToken(string token) => _token = token;

        /// <summary>
        /// Gets the <see cref="CookieContainer"/>.
        /// </summary>
        public CookieContainer Cookies { get; }

        /// <summary>
        /// Issues a GET request to the relative url on <see cref="TestServer.BaseAddress"/>.
        /// </summary>
        /// <param name="relativeUrl">The relative url.</param>
        /// <returns>The response.</returns>
        public HttpResponseMessage Get(string relativeUrl)
        {
            return Get(new Uri(relativeUrl, UriKind.Relative));
        }

        /// <summary>
        /// Issues a GET request to the relative url on <see cref="TestServer.BaseAddress"/>.
        /// </summary>
        /// <param name="relativeUrl">The relative url.</param>
        /// <returns>The response.</returns>
        public HttpResponseMessage Get(Uri relativeUrl)
        {
            var absoluteUrl = new Uri(_testServer.BaseAddress, relativeUrl);
            var requestBuilder = _testServer.CreateRequest(absoluteUrl.ToString());
            AddCookies(requestBuilder, absoluteUrl);
            AddToken(requestBuilder);
            var response = requestBuilder.GetAsync().Result;
            UpdateCookies(response, absoluteUrl);
            return response;
        }

        /// <summary>
        /// Issues a POST request to the relative url on <see cref="TestServer.BaseAddress"/> with form values.
        /// </summary>
        /// <param name="relativeUrl">The relative url.</param>
        /// <param name="formValues">The form values.</param>
        /// <returns>The response.</returns>
        public HttpResponseMessage Post(string relativeUrl, IDictionary<string, string> formValues)
        {
            return Post(new Uri(relativeUrl, UriKind.Relative), formValues);
        }

        /// <summary>
        /// Issues a POST request to the relative url on <see cref="TestServer.BaseAddress"/> with form values.
        /// </summary>
        /// <param name="relativeUrl">The relative url.</param>
        /// <param name="formValues">The form values.</param>
        /// <returns>The response.</returns>
        public HttpResponseMessage Post(Uri relativeUrl, IDictionary<string, string> formValues)
        {
            return Post(relativeUrl, new FormUrlEncodedContent(formValues));
        }

        /// <summary>
        /// Issues a POST request to the relative url on <see cref="TestServer.BaseAddress"/> with an "application/json"
        /// contnent.
        /// </summary>
        /// <param name="relativeUrl">The relative url.</param>
        /// <param name="json">The json content.</param>
        /// <returns>The response.</returns>
        public HttpResponseMessage Post(string relativeUrl, string json)
        {
            var c = new StringContent(json);
            c.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json");
            return Post(new Uri(relativeUrl, UriKind.Relative), c);
        }

        /// <summary>
        /// Issues a POST request to the relative url on <see cref="TestServer.BaseAddress"/> with an <see cref="HttpContent"/>.
        /// </summary>
        /// <param name="relativeUrl">The relative url.</param>
        /// <param name="content">The content.</param>
        /// <returns>The response.</returns>
        public HttpResponseMessage Post(Uri relativeUrl, HttpContent content)
        {
            var absoluteUrl = new Uri(_testServer.BaseAddress, relativeUrl);
            var requestBuilder = _testServer.CreateRequest(absoluteUrl.ToString());
            AddCookies(requestBuilder, absoluteUrl);
            AddToken(requestBuilder);
            var response = requestBuilder.And(message =>
            {
                message.Content = content;
            }).PostAsync().Result;
            UpdateCookies(response, absoluteUrl);
            return response;
        }

        /// <summary>
        /// Follows the reddirected url if the response's status is <see cref="HttpStatusCode.Moved"/> (301) 
        /// or <see cref="HttpStatusCode.Found"/> (302).
        /// A redirection always uses the GET method.
        /// </summary>
        /// <param name="response">The initial response.</param>
        /// <returns>The redirected response.</returns>
        public HttpResponseMessage FollowRedirect(HttpResponseMessage response)
        {
            if (response.StatusCode != HttpStatusCode.Moved && response.StatusCode != HttpStatusCode.Found)
            {
                return response;
            }
            var redirectUrl = new Uri(response.Headers.Location.ToString(), UriKind.RelativeOrAbsolute);
            if (redirectUrl.IsAbsoluteUri)
            {
                redirectUrl = new Uri(redirectUrl.PathAndQuery, UriKind.Relative);
            }
            return Get(redirectUrl);
        }

        void AddToken(RequestBuilder requestBuilder)
        {
            if(_token != null)
            {
                requestBuilder.AddHeader(AuthorizationHeaderName, "Bearer " + _token );
            }
        }

        void AddCookies(RequestBuilder requestBuilder, Uri absoluteUrl)
        {
            var cookieHeader = Cookies.GetCookieHeader(absoluteUrl);
            if (!string.IsNullOrWhiteSpace(cookieHeader))
            {
                requestBuilder.AddHeader(HeaderNames.Cookie, cookieHeader);
            }
        }

        void UpdateCookies(HttpResponseMessage response, Uri absoluteUrl)
        {
            if (response.Headers.Contains(HeaderNames.SetCookie))
            {
                var cookies = response.Headers.GetValues(HeaderNames.SetCookie);
                foreach (var cookie in cookies)
                {
                    Cookies.SetCookies( absoluteUrl, cookie);
                }
            }
        }
    }
}

