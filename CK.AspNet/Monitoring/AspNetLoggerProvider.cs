using System;
using CK.Monitoring;
using Microsoft.Extensions.Logging;

namespace CK.AspNet
{
    /// <summary>
    /// This <see cref="ILoggerProvider"/> implementation routes
    /// logs to GrandOutput.ExternalLogs.
    /// </summary>
    sealed class AspNetLoggerProvider : ILoggerProvider
    {
        readonly GrandOutput _grandOutput;
        internal bool Running;

        public AspNetLoggerProvider( GrandOutput grandOutput )
        {
            _grandOutput = grandOutput;
        }

        ILogger ILoggerProvider.CreateLogger( string categoryName )
        {
            return new AspNetLogger( this, categoryName, _grandOutput );
        }

        void IDisposable.Dispose()
        {
            Running = false;
        }
    }
}
