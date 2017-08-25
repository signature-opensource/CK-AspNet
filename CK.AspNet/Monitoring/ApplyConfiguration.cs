using CK.Monitoring;
using System;
using Microsoft.Extensions.Configuration;

namespace Microsoft.AspNetCore.Hosting
{
    class ApplyConfiguration<TConfig> where TConfig : GrandOutputOptions
    {
        IDisposable _changeToken;
        readonly IConfiguration _config;
        readonly string _configurationSection;

        public ApplyConfiguration( IConfiguration config, string configurationSection )
        {
            _config = config;
            _configurationSection = configurationSection;
        }

        public IConfiguration Config => _config;

        public string ConfigPath => _configurationSection;

        public IConfigurationSection Section => Config.GetSection( ConfigPath );

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
                var updatedGrandOutputConfiguration = updatedConfiguration.CreateGrandOutputConfiguration();
                GrandOutput.Default.ApplyConfiguration( updatedGrandOutputConfiguration, true );
            }

            applyConfigurationInfo.RegisterChangeCallback();
        }
    }
}
