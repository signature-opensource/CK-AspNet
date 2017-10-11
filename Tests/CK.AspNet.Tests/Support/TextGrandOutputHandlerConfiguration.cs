using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CK.Core;
using CK.Monitoring;
using System.Threading;

namespace CK.AspNet.Tests
{
    /// <summary>
    /// This configuration object can be used to get the current text
    /// of all the monitors.
    /// </summary>
    public class TextGrandOutputHandlerConfiguration : IHandlerConfiguration
    {
        readonly TextGrandOutputHandlerConfiguration _origin;
        object _lock = new object();
        string _result;
        bool _wantResult;
        bool _end;

        /// <summary>
        /// Initializes a new configuration.
        /// </summary>
        public TextGrandOutputHandlerConfiguration()
        {
        }

        /// <summary>
        /// Gets the current text collected from the handler.
        /// </summary>
        /// <returns></returns>
        public string GetText()
        {
            string result;
            lock( _lock )
            {
                _wantResult = true;
                while( _result == null )
                {
                    Monitor.Wait( _lock );
                }
                result = _result;
                if( !_end ) _result = null;
            }
            return result;
        }

        internal void FromSink( StringBuilder b, bool end )
        {
            if( _origin != null ) _origin.FromSink( b, end );
            lock( _lock )
            {
                if( _wantResult || (_end = end))
                {
                    _wantResult = false;
                    _result = b.ToString();
                    Monitor.Pulse( _lock );
                }
            }
        }

        TextGrandOutputHandlerConfiguration( TextGrandOutputHandlerConfiguration origin )
        {
            _origin = origin;
        }

        /// <summary>
        /// Clones a linked configuration here so that this configuration object or the original one can be used to 
        /// retrieve the current handler text.
        /// </summary>
        /// <returns>A clone object.</returns>
        public IHandlerConfiguration Clone() => new TextGrandOutputHandlerConfiguration( this );
    }
}
