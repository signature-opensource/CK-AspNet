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

        public ValueTask<bool> ActivateAsync( IActivityMonitor m )
        {
            m.Info( $"Activating: {_config.Message}." );
            return ValueTask.FromResult( true );
        }

        public ValueTask<bool> ApplyConfigurationAsync( IActivityMonitor m, IHandlerConfiguration c )
        {
            if( c is HandlerWithConfigSectionConfiguration conf )
            {
                m.Info( $"Applying: {_config.Message} => {conf.Message}." );
                _config = conf;
                return ValueTask.FromResult( true );
            }
            return ValueTask.FromResult( false );
        }

        public ValueTask DeactivateAsync( IActivityMonitor m ) => ValueTask.CompletedTask;

        public ValueTask HandleAsync( IActivityMonitor m, IMulticastLogEntry logEvent ) => ValueTask.CompletedTask;

        public ValueTask OnTimerAsync( IActivityMonitor m, TimeSpan timerSpan ) => ValueTask.CompletedTask;
    }
}
