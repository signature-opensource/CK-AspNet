using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CK.Testing
{
    public sealed partial class RunningAspNetServer
    {
        /// <summary>
        /// Basic client that must not be used concurrently.
        /// <para>
        /// This doesn't try to follow the redirect (300 to 399) status code will be returned.
        /// The cookie management attempts to fix https://github.com/dotnet/corefx/issues/21250#issuecomment-309613552,
        /// <see cref="CookieContainer"/> can be changed as well as the <see cref="Token"/>.
        /// </para>
        /// </summary>
        public sealed partial class RunningClient
        {
            readonly Uri _baseAddress;
            readonly HttpClient _httpClient;
            readonly CookieContainer _cookieContainer;
            readonly RunningAspNetServer _server;
            string? _token;

            sealed partial class Handler : DelegatingHandler
            {
                readonly RunningClient _client;

                public Handler( RunningClient client )
                    : base( new HttpClientHandler() { AllowAutoRedirect = false, UseCookies = false } )
                {
                    _client = client;
                }

                protected override async Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken )
                {
                    Uri requestUri = request.RequestUri!;
                    if( _client.Token != null && _client._baseAddress.IsBaseOf( requestUri ) )
                    {
                        request.Headers.Add( HeaderNames.Authorization, "Bearer " + _client.Token );
                    }
                    var cookies = _client._cookieContainer.GetCookieHeader( requestUri );
                    if( !String.IsNullOrWhiteSpace( cookies ) )
                    {
                        request.Headers.Add( HeaderNames.Cookie, cookies );
                    }
                    var r = await base.SendAsync( request, cancellationToken );
                    UpdateCookiesWithPathHandling( _client._cookieContainer, r, requestUri );
                    return r;
                }

                static readonly Regex _rCookiePath = CookiePath();
                [GeneratedRegex( "(?<=^|;)\\s*path\\s*=\\s*(?<p>[^;\\s]*)\\s*;?", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant )]
                private static partial Regex CookiePath();

                /// <summary>
                /// Corrects CookieContainer behavior.
                /// See: https://github.com/dotnet/corefx/issues/21250#issuecomment-309613552
                /// This fix the Cookie path bug of the CookieContainer but does not handle any other
                /// specification from current (since 2011) https://tools.ietf.org/html/rfc6265.
                /// </summary>
                /// <param name="container">The cookie container to update.</param>
                /// <param name="response">A response message.</param>
                static void UpdateCookiesWithPathHandling( CookieContainer container, HttpResponseMessage response, Uri requestUri )
                {
                    if( response.Headers.TryGetValues( HeaderNames.SetCookie, out var cookies ) )
                    {
                        var root = new Uri( requestUri.GetLeftPart( UriPartial.Authority ) );
                        foreach( var cookie in cookies )
                        {
                            string cFinal = cookie;
                            Uri? rFinal = null;
                            Match m = _rCookiePath.Match( cookie );
                            while( m.Success )
                            {
                                // Last Path wins: see https://tools.ietf.org/html/rfc6265#section-5.3 §7.
                                cFinal = cFinal.Remove( m.Index, m.Length );
                                rFinal = new Uri( root, m.Groups[1].Value );
                                m = m.NextMatch();
                            }
                            if( rFinal == null )
                            {
                                // No path specified in cookie: the path is the one of the request.
                                rFinal = new Uri( requestUri.GetLeftPart( UriPartial.Path ) );
                            }
                            container.SetCookies( rFinal, cFinal );
                        }
                    }
                }

            }

            internal RunningClient( RunningAspNetServer server )
            {
                _baseAddress = new Uri( server._serverAddress );
                _httpClient = new HttpClient( new Handler( this ) ) { BaseAddress = _baseAddress };
                _cookieContainer = new CookieContainer();
                _server = server;
            }

            internal void Dispose()
            {
                _httpClient.Dispose();
            }

            /// <summary>
            /// Gets the server.
            /// </summary>
            public RunningAspNetServer Server => _server;

            /// <summary>
            /// Gets the <see cref="ServerAddress"/> as an <see cref="Uri"/>.
            /// </summary>
            public Uri BaseAddress => _baseAddress;

            /// <summary>
            /// Gets or sets a token to be sent with the requests.
            /// </summary>
            public string? Token
            {
                get => _token;
                set => _token = value;
            }

            /// <summary>
            /// Gets the cookie container.
            /// </summary>
            public CookieContainer CookieContainer => _cookieContainer;

            /// <summary>
            /// Issues a GET request to the relative url on <see cref="BaseAddress"/> or to an absolute url.
            /// </summary>
            /// <param name="url">The BaseAddress relative url or an absolute url.</param>
            /// <returns>The response.</returns>
            public Task<HttpResponseMessage> GetAsync( string url, CancellationToken cancellation = default )
            {
                return GetAsync( new Uri( url, UriKind.RelativeOrAbsolute ), cancellation );
            }

            /// <summary>
            /// Issues a GET request to the relative url on <see cref="BaseAddress"/> or to an absolute url.
            /// </summary>
            /// <param name="url">The BaseAddress relative url or an absolute url.</param>
            /// <returns>The response.</returns>
            public Task<HttpResponseMessage> GetAsync( Uri url, CancellationToken cancellation = default )
            {
                var absoluteUrl = new Uri( BaseAddress, url );
                return _httpClient.GetAsync( absoluteUrl, cancellation );
            }

            /// <summary>
            /// Issues a POST request to the relative url on <see cref="BaseAddress"/> or to an absolute url
            /// with form values.
            /// </summary>
            /// <param name="url">The BaseAddress relative url or an absolute url.</param>
            /// <param name="formValues">The form values.</param>
            /// <returns>The response.</returns>
            public Task<HttpResponseMessage> PostAsync( string url, IEnumerable<KeyValuePair<string, string>> formValues, CancellationToken cancellation = default )
            {
                return PostAsync( new Uri( url, UriKind.RelativeOrAbsolute ), formValues, cancellation );
            }

            /// <summary>
            /// Issues a POST request to the relative url on <see cref="BaseAddress"/> or to an absolute url 
            /// with an "application/json" content.
            /// </summary>
            /// <param name="url">The BaseAddress relative url or an absolute url.</param>
            /// <param name="json">The json content.</param>
            /// <returns>The response.</returns>
            public Task<HttpResponseMessage> PostJsonAsync( string url, string json, CancellationToken cancellation = default ) => PostJsonAsync( new Uri( url, UriKind.RelativeOrAbsolute ), json, cancellation );

            /// <summary>
            /// Issues a POST request to the relative url on <see cref="BaseAddress"/> or to an absolute url 
            /// with an "application/json; charset=utf-8" content.
            /// </summary>
            /// <param name="url">The BaseAddress relative url or an absolute url.</param>
            /// <param name="json">The json content.</param>
            /// <returns>The response.</returns>
            public Task<HttpResponseMessage> PostJsonAsync( Uri url, string json, CancellationToken cancellation = default )
            {
                var c = new StringContent( json, Encoding.UTF8, "application/json" );
                return PostAsync( url, c, cancellation );
            }

            /// <summary>
            /// Issues a POST request to the relative url on <see cref="BaseAddress"/> or to an absolute url 
            /// with an "application/xml; charset=utf-8" content.
            /// </summary>
            /// <param name="url">The BaseAddress relative url or an absolute url.</param>
            /// <param name="xml">The xml content.</param>
            /// <returns>The response.</returns>
            public Task<HttpResponseMessage> PostXmlAsync( string url, string xml, CancellationToken cancellation = default ) => PostXmlAsync( new Uri( url, UriKind.RelativeOrAbsolute ), xml, cancellation );

            /// <summary>
            /// Issues a POST request to the relative url on <see cref="BaseAddress"/> or to an absolute url 
            /// with an "application/xml; charset=utf-8" content.
            /// </summary>
            /// <param name="url">The BaseAddress relative url or an absolute url.</param>
            /// <param name="xml">The xml content.</param>
            /// <returns>The response.</returns>
            public Task<HttpResponseMessage> PostXmlAsync( Uri url, string xml, CancellationToken cancellation = default )
            {
                var c = new StringContent( xml, Encoding.UTF8, "application/xml" );
                return PostAsync( url, c,cancellation );
            }

            /// <summary>
            /// Issues a POST request to the relative url on <see cref="BaseAddress"/> or to an absolute url
            /// with form values.
            /// </summary>
            /// <param name="url">The BaseAddress relative url or an absolute url.</param>
            /// <param name="formValues">The form values (compatible with a IDictionary&lt;string, string&gt;).</param>
            /// <returns>The response.</returns>
            public Task<HttpResponseMessage> PostAsync( Uri url, IEnumerable<KeyValuePair<string, string>> formValues, CancellationToken cancellation = default )
            {
                return PostAsync( url, new FormUrlEncodedContent( formValues ), cancellation );
            }

            /// <summary>
            /// Issues a POST request to the relative url on <see cref="BaseAddress"/> or to an absolute url 
            /// with an <see cref="HttpContent"/>.
            /// </summary>
            /// <param name="url">The BaseAddress relative url or an absolute url.</param>
            /// <param name="content">The content.</param>
            /// <returns>The response.</returns>
            public Task<HttpResponseMessage> PostAsync( Uri url, HttpContent content, CancellationToken cancellation = default ) => _httpClient.PostAsync( url, content, cancellation );

            /// <summary>
            /// Issues a GET request to therelative url on <see cref="BaseAddress"/> or to an absolute url
            /// and return the response body as a byte array in an asynchronous operation.
            /// </summary>
            /// <param name="url">The url.</param>
            /// <returns>The byte array.</returns>
            public Task<byte[]> GetByteArrayAsync( string url, CancellationToken cancellation = default ) => GetByteArrayAsync( new Uri( url, UriKind.RelativeOrAbsolute ), cancellation );

            /// <summary>
            /// Issues a GET request to therelative url on <see cref="BaseAddress"/> or to an absolute url
            /// and return the response body as a byte array in an asynchronous operation.
            /// </summary>
            /// <param name="url">The url.</param>
            /// <returns>The byte array.</returns>
            public Task<byte[]> GetByteArrayAsync( Uri url, CancellationToken cancellation = default ) => _httpClient.GetByteArrayAsync( url, cancellation );

            /// <summary>
            /// Issues a GET request to therelative url on <see cref="BaseAddress"/> or to an absolute url
            /// and return a stream on the response body (headers have been read).
            /// </summary>
            /// <param name="url">The url.</param>
            /// <returns>The opened stream.</returns>
            public Task<Stream> GetStreamAsync( string url, CancellationToken cancellation = default ) => GetStreamAsync( new Uri( url, UriKind.RelativeOrAbsolute ), cancellation );

            /// <summary>
            /// Issues a GET request to therelative url on <see cref="BaseAddress"/> or to an absolute url
            /// and return a stream on the response body (headers have been read).
            /// </summary>
            /// <param name="url">The url.</param>
            /// <returns>The byte array.</returns>
            public Task<Stream> GetStreamAsync( Uri url, CancellationToken cancellation = default ) => _httpClient.GetStreamAsync( url, cancellation );

            /// <summary>
            /// Issues a GET request to therelative url on <see cref="BaseAddress"/> or to an absolute url
            /// and return the body as a string.
            /// </summary>
            /// <param name="url">The url.</param>
            /// <returns>The string.</returns>
            public Task<string> GetStringAsync( string url, CancellationToken cancellation = default ) => GetStringAsync( new Uri( url, UriKind.RelativeOrAbsolute ), cancellation );

            /// <summary>
            /// Issues a GET request to therelative url on <see cref="BaseAddress"/> or to an absolute url
            /// and return a stream on the response body (headers have been read).
            /// </summary>
            /// <param name="url">The url.</param>
            /// <returns>The byte array.</returns>
            public Task<string> GetStringAsync( Uri url, CancellationToken cancellation = default ) => _httpClient.GetStringAsync( url, cancellation );

        }
    }

}
