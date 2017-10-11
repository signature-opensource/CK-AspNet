using CK.Monitoring;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CK.Core;

namespace CK.AspNet.Tests
{

    public class HandlerWithConfigSection : IGrandOutputHandler
    {
        HandlerWithConfigSectionConfiguration _config;

        public HandlerWithConfigSection( HandlerWithConfigSectionConfiguration c )
        {
            _config = c;
        }

        public bool Activate( IActivityMonitor m )
        {
            m.Info( $"Activating: {_config.Message}." );
            return true;
        }

        public bool ApplyConfiguration( IActivityMonitor m, IHandlerConfiguration c )
        {
            if( c is HandlerWithConfigSectionConfiguration conf )
            {
                m.Info( $"Applying: {_config.Message} => {conf.Message}." );
                _config = conf;
                return true;
            }
            return false;
        }

        public void Deactivate( IActivityMonitor m )
        {
        }

        public void Handle( IActivityMonitor m, GrandOutputEventInfo logEvent )
        {
        }

        public void OnTimer( IActivityMonitor m, TimeSpan timerSpan )
        {
        }
    }
}
