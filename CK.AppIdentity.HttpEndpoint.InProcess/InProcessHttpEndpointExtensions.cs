using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CK.AppIdentity;

/// <summary>
/// Gives access to the in-process server of an endpoint (<see cref="InProcessHttpEndpointOption"/>).
/// </summary>
public static class InProcessHttpEndpointExtensions
{
    /// <summary>
    /// Gets the in-process server of this endpoint, or null when it runs on Kestrel. Its
    /// <see cref="TestServer.SendAsync(System.Action{Microsoft.AspNetCore.Http.HttpContext}, System.Threading.CancellationToken)"/>
    /// sends a request through the pipeline of the endpoint.
    /// </summary>
    /// <param name="endpoint">This running endpoint.</param>
    /// <returns>The in-process server or null.</returns>
    public static TestServer? GetInProcessServer( this RunningHttpEndpoint endpoint )
    {
        return endpoint.Application.Services.GetService<IServer>() as TestServer;
    }
}
