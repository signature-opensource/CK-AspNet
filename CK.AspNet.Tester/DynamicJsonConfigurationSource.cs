using Microsoft.Extensions.Configuration.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.AspNet.Tester
{
    /// <summary>
    /// Simple <see cref="JsonConfigurationSource"/> that is bound to an in-memory
    /// basic file provider.
    /// <see cref="SetJson(string)"/> triggers a change of the configuration source.
    /// </summary>
    public class DynamicJsonConfigurationSource : JsonConfigurationSource
    {
        readonly BasicMemoryFileProvider _f;

        /// <summary>
        /// Initializes a new Json configuration source object with an initial json.
        /// </summary>
        /// <param name="initialText">The initial json value.</param>
        public DynamicJsonConfigurationSource( string initialText = "" )
        {
            _f = new BasicMemoryFileProvider();
            _f.Set( "config", initialText ?? String.Empty );
            FileProvider = _f;
            Path = "config";
            ReloadOnChange = true;
            ReloadDelay = 1;
        }

        /// <summary>
        /// Sets a new json. This will trigger a configuration change.
        /// </summary>
        /// <param name="text">The new json text.</param>
        public void SetJson( string text ) => _f.Set( "config", text );

        /// <summary>
        /// Deletes the configuration.
        /// It can be set again if needed.
        /// </summary>
        public void Delete() => _f.Delete( "config" );

    }
}
