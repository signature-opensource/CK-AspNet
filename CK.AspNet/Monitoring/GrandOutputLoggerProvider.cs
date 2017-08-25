using CK.Monitoring;
using Microsoft.Extensions.Logging;

namespace CK.AspNet
{
    /// <summary>
    /// The <see cref="GrandOutput"/> <see cref="ILoggerProvider"/> implementation.
    /// </summary>
    public class GrandOutputLoggerProvider : ILoggerProvider
    {
        readonly GrandOutput _grandOutput;
        readonly bool _disposeGrandOutput;

        /// <summary>
        /// Creates a new <see cref="GrandOutputLoggerProvider"/>
        /// </summary>
        /// <param name="grandOutput"></param>
        /// <param name="disposeGrandOutput"></param>
        public GrandOutputLoggerProvider( GrandOutput grandOutput, bool disposeGrandOutput = false )
        {
            _grandOutput = grandOutput;
            _disposeGrandOutput = disposeGrandOutput;
        }

        /// <summary>
        /// Creates a <see cref="GrandOutputLogger"/>
        /// </summary>
        /// <param name="categoryName"></param>
        /// <returns></returns>
        public ILogger CreateLogger( string categoryName )
        {
            return new GrandOutputLogger( categoryName, _grandOutput );
        }

        /// <summary>
        /// Disposes the <see cref="GrandOutput"/> only if specified
        /// </summary>
        public void Dispose()
        {
            if( _disposeGrandOutput && !_grandOutput.IsDisposed ) _grandOutput.Dispose();
        }
    }
}
