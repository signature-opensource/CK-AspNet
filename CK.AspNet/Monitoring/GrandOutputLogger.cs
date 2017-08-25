using CK.Monitoring;
using Microsoft.Extensions.Logging;
using System;

namespace CK.AspNet
{
    /// <summary>
    /// The <see cref="ILogger"/> for the <see cref="GrandOutput"/>.
    /// </summary>
    public class GrandOutputLogger : ILogger
    {
        readonly string _categoryName;
        readonly GrandOutput _output;

        /// <summary>
        /// Creates a new <see cref="GrandOutputLogger"/> with the given category name.
        /// </summary>
        /// <param name="categoryName"></param>
        /// <param name="output"></param>
        public GrandOutputLogger( string categoryName, GrandOutput output )
        {
            _categoryName = categoryName ?? String.Empty;
            _output = output;
        }

        /// <summary>
        /// Justs logs the state of the new scope as an external log.
        /// </summary>
        /// <typeparam name="TState"></typeparam>
        /// <param name="state"></param>
        /// <returns></returns>
        public IDisposable BeginScope<TState>( TState state )
        {
            _output.ExternalLog( Core.LogLevel.Trace, state?.ToString() );

            return CK.Core.Util.EmptyDisposable;
        }

        /// <summary>
        /// Alwasy true.
        /// </summary>
        /// <param name="logLevel"></param>
        /// <returns></returns>
        public bool IsEnabled( LogLevel logLevel )
        {
            return true;
        }

        /// <summary>
        /// Logs to the <see cref="GrandOutput"/> as an external log entry.
        /// </summary>
        /// <typeparam name="TState"></typeparam>
        /// <param name="logLevel"></param>
        /// <param name="eventId"></param>
        /// <param name="state"></param>
        /// <param name="exception"></param>
        /// <param name="formatter"></param>
        public void Log<TState>( LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter )
        {
            _output.ExternalLog( GetLogLevel( logLevel ), $"[{_categoryName}] {formatter( state, exception )}", exception);
        }

        static CK.Core.LogLevel GetLogLevel( LogLevel ll )
        {
            switch( ll )
            {
                case LogLevel.Debug: return CK.Core.LogLevel.Debug;
                case LogLevel.Trace: return CK.Core.LogLevel.Trace;
                case LogLevel.Information: return CK.Core.LogLevel.Info;
                case LogLevel.Warning: return CK.Core.LogLevel.Warn;
                case LogLevel.Error: return CK.Core.LogLevel.Error;
                case LogLevel.Critical: return CK.Core.LogLevel.Fatal;
            }
            return CK.Core.LogLevel.None;
        }
    }
}
