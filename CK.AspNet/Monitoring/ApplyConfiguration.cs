using CK.Monitoring;
using System;
using Microsoft.Extensions.Configuration;

namespace Microsoft.AspNetCore.Hosting
{
    class ApplyConfiguration<TConfig> where TConfig : GrandOutputOptions
    {
        IDisposable _changeToken;
        readonly IConfigurationSection _section;

        public ApplyConfiguration( IConfigurationSection section )
        {
            _section = section;
        }

        public IConfigurationSection Section => _section;

        public TConfig GetConfiguration() => Section.Get<TConfig>();

        internal void RegisterChangeCallback()
        {
            // Disposes the previous change token
            _changeToken?.Dispose();

            var reloadToken = Section.GetReloadToken();
            _changeToken = reloadToken.RegisterChangeCallback( ApplyNewConfiguration, this );
        }

        void ApplyNewConfiguration( object state )
        {
            ApplyConfiguration<TConfig> applyConfigurationInfo = (ApplyConfiguration<TConfig>)state;
            if( GrandOutput.Default != null )
            {
                var updatedConfiguration = applyConfigurationInfo.GetConfiguration();
                var updatedGrandOutputConfiguration = updatedConfiguration.CreateGrandOutputConfiguration( Section );
                GrandOutput.Default.ApplyConfiguration( updatedGrandOutputConfiguration, true );
            }

            applyConfigurationInfo.RegisterChangeCallback();
        }
    }
}
