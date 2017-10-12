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
        readonly MulticastLogEntryTextBuilder _builder = new MulticastLogEntryTextBuilder();

        /// <summary>
        /// Initializes a new <see cref="TextGrandOutputHandler"/>.
        /// </summary>
        /// <param name="config">The configuration object.</param>
        public TextGrandOutputHandler( TextGrandOutputHandlerConfiguration config )
        {
            _config = config;
        }

        bool IGrandOutputHandler.Activate( IActivityMonitor m ) => true;

        bool IGrandOutputHandler.ApplyConfiguration( IActivityMonitor m, IHandlerConfiguration c )
        {
            TextGrandOutputHandlerConfiguration config = c as TextGrandOutputHandlerConfiguration;
            if( config != null )
            {
                _config = config;
                return true;
            }
            return false;
        }

        void IGrandOutputHandler.Deactivate( IActivityMonitor m ) => _config.FromSink( _builder.Builder, true );

        void IGrandOutputHandler.Handle( IActivityMonitor m, GrandOutputEventInfo logEvent )
        {
            _builder.AppendEntry( logEvent.Entry );
            _config.FromSink( _builder.Builder, false );
        }

        void IGrandOutputHandler.OnTimer( IActivityMonitor m, TimeSpan timerSpan ) => _config.FromSink( _builder.Builder, false );

    }
}
