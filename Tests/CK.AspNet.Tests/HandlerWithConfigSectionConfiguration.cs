using CK.Monitoring;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.AspNet.Tests
{
    /// <summary>
    /// This configuration object initializes itself manually from a
    /// configuration section. It is not <see cref="IConfigurationSection.Bind"/>
    /// 
    /// </summary>
    public class HandlerWithConfigSectionConfiguration : IHandlerConfiguration
    {
        /// <summary>
        /// The default constructor can be private.
        /// </summary>
        HandlerWithConfigSectionConfiguration()
        {
        }

        /// <summary>
        /// Copy constructor (clone support).
        /// Required because of the private setter.
        /// </summary>
        /// <param name="o">The source configuration.</param>
        HandlerWithConfigSectionConfiguration( HandlerWithConfigSectionConfiguration o )
        {
            Message = o.Message;
        }

        public HandlerWithConfigSectionConfiguration( IConfigurationSection s )
        {
            Message = s["Message"];
        }

        public string Message { get; private set; }

        public IHandlerConfiguration Clone()
        {
            return new HandlerWithConfigSectionConfiguration( this );
        }
    }
}
