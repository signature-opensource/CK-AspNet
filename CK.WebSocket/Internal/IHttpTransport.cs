using CK.Core;
using Microsoft.AspNetCore.Http;

namespace CK.WebSocket;

internal interface IHttpTransport
{
    /// <summary>
    /// Executes the transport
    /// </summary>
    /// <param name="monitor"></param>
    /// <param name="context"></param>
    /// <param name="token"></param>
    /// <returns>A <see cref="Task"/> that completes when the transport has finished processing</returns>
    Task<bool> ProcessRequestAsync( ActivityMonitor.Token monitor, HttpContext context, CancellationToken token );
}
