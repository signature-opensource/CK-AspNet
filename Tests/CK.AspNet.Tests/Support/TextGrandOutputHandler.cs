using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CK.Core;
using CK.Monitoring;

namespace CK.AspNet.Tests
{
    /// <summary>
    /// Handler associated to <see cref="TextGrandOutputHandlerConfiguration"/>.
    /// </summary>
    public class TextGrandOutputHandler : IGrandOutputHandler
    {
        TextGrandOutputHandlerConfiguration _config;
        readonly MulticastLogEntryTextBuilder _formatter = new MulticastLogEntryTextBuilder( false, false );
        readonly StringBuilder _builder = new StringBuilder();

        /// <summary>
        /// Initializes a new <see cref="TextGrandOutputHandler"/>.
        /// </summary>
        /// <param name="config">The configuration object.</param>
        public TextGrandOutputHandler( TextGrandOutputHandlerConfiguration config )
        {
            _config = config;
            _builder = new StringBuilder();
        }

        ValueTask<bool> IGrandOutputHandler.ActivateAsync( IActivityMonitor m ) => ValueTask.FromResult( true );

        ValueTask<bool> IGrandOutputHandler.ApplyConfigurationAsync(IActivityMonitor m, IHandlerConfiguration c)
        {
            TextGrandOutputHandlerConfiguration config = c as TextGrandOutputHandlerConfiguration;
            if( config != null )
            {
                _config = config;
                return ValueTask.FromResult( true );
            }
            return ValueTask.FromResult( false );
        }

        ValueTask IGrandOutputHandler.DeactivateAsync( IActivityMonitor m )
        {
            _config.FromSink( _builder, true );
            return ValueTask.CompletedTask;
        }

        ValueTask IGrandOutputHandler.HandleAsync(IActivityMonitor m, InputLogEntry logEvent)
        {
            _builder.Append( _formatter.FormatEntryString( logEvent ) );
            _config.FromSink( _builder, false );
            return ValueTask.CompletedTask;
        }

        ValueTask IGrandOutputHandler.OnTimerAsync( IActivityMonitor m, TimeSpan timerSpan )
        {
            _config.FromSink( _builder, false );
            return ValueTask.CompletedTask;
        }

    }
}
