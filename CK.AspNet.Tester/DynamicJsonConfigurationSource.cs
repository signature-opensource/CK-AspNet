using Microsoft.Extensions.Configuration.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.AspNet.Tester
{
    public class DynamicJsonConfigurationSource : JsonConfigurationSource
    {
        readonly BasicMemoryFileProvider _f;

        public DynamicJsonConfigurationSource( string initialText )
        {
            _f = new BasicMemoryFileProvider();
            _f.Set( "config", initialText );
            FileProvider = _f;
            Path = "config";
            ReloadOnChange = true;
            ReloadDelay = 1;
        }

        public void SetJson( string text ) => _f.Set( "config", text );
    }
}
