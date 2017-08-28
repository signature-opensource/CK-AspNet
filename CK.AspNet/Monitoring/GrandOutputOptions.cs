using CK.Core;
using CK.Monitoring;
using System;
using Microsoft.Extensions.Configuration;
using CK.Monitoring.Handlers;

namespace Microsoft.AspNetCore.Hosting
{
    public class GrandOutputOptions
    {
        public string RootLogPath { get; set; } = "Logs";
#if NET461
        public bool HandleDiagnosticsEvents { get; set; } = true;
#endif
        public TextFileConfiguration TextFile { get; set; }
        public BinaryFileConfiguration BinaryFile { get; set; }
        public TimeSpan TimerDuration { get; set; } = TimeSpan.FromMilliseconds( 500 );

        public bool LogUnhandledExceptions { get; set; } = true;

        /// <summary>
        /// Creates the <see cref="GrandOutputConfiguration"/>.
        /// </summary>
        /// <param name="configuration">A configuration section where data needed to configures the grand output are stored. Can be null.</param>
        /// <returns></returns>
        public virtual GrandOutputConfiguration CreateGrandOutputConfiguration( IConfigurationSection configuration )
        {
            var config = new GrandOutputConfiguration();
            if( TextFile != null ) config.Handlers.Add( TextFile );
            if( BinaryFile != null ) config.Handlers.Add( BinaryFile );
            config.TimerDuration = TimerDuration;
            return config;
        }
    }
}
