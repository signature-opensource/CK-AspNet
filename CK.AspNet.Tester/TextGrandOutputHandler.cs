using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CK.Core;
using CK.Monitoring;

namespace CK.AspNet.Tester
{
    public class TextGrandOutputHandler : IGrandOutputHandler
    {
        TextGrandOutputHandlerConfiguration _config;
        readonly MulticastLogEntryTextBuilder _builder = new MulticastLogEntryTextBuilder();

        public TextGrandOutputHandler( TextGrandOutputHandlerConfiguration config )
        {
            _config = config;
        }

        public bool Activate( IActivityMonitor m )
        {
            return true;
        }

        public bool ApplyConfiguration( IActivityMonitor m, IHandlerConfiguration c )
        {
            TextGrandOutputHandlerConfiguration config = c as TextGrandOutputHandlerConfiguration;
            if( config != null )
            {
                _config = config;
                return true;
            }
            return false;
        }

        public void Deactivate( IActivityMonitor m )
        {
            _config.FromSink( _builder.Builder, true );
        }

        public void Handle( GrandOutputEventInfo logEvent )
        {
            _builder.AppendEntry( logEvent.Entry );
            _config.FromSink( _builder.Builder, false );
        }

        public void OnTimer( TimeSpan timerSpan )
        {
            _config.FromSink( _builder.Builder, false );
        }
    }
}
