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

        public virtual GrandOutputConfiguration CreateGrandOutputConfiguration()
        {
            var config = new GrandOutputConfiguration();
            if( TextFile != null ) config.Handlers.Add( TextFile );
            if( BinaryFile != null ) config.Handlers.Add( BinaryFile );
            config.TimerDuration = TimerDuration;
            return config;
        }
    }
}
