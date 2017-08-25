using CK.Monitoring;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.AspNet
{
    /// <summary>
    /// Extensions methods
    /// </summary>
    public static class GrandOutputExtensions
    {
        /// <summary>
        /// Adds the grand output to the given <see cref="ILoggingBuilder"/>
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="grandOutput"></param>
        /// <returns></returns>
        static public ILoggingBuilder AddGrandOutput( this ILoggingBuilder builder, GrandOutput grandOutput ) 
        {
            return builder.AddProvider( new GrandOutputLoggerProvider( grandOutput, false ) );
        }

        //static public ILoggerFactory AddGrandOutput( this ILoggerFactory loggerFactory, GrandOutputConfiguration grandOutputConfiguration )
        //{
        //    if( grandOutputConfiguration == null ) throw new ArgumentNullException( nameof( grandOutputConfiguration ) );

        //    loggerFactory.AddProvider( new GrandOutputLoggerProvider( new GrandOutput( grandOutputConfiguration, true ), true ) );
        //    return loggerFactory;
        //}
        //static public ILoggerFactory AddGrandOutput( this ILoggerFactory loggerFactory, Action<GrandOutputConfiguration> grandOutputConfiguration )
        //{
        //    if( grandOutputConfiguration == null ) throw new ArgumentNullException( nameof( grandOutputConfiguration ) );

        //    var config = new GrandOutputConfiguration();
        //    grandOutputConfiguration( config );
        //    return loggerFactory.AddGrandOutput( config );
        //}
        //static public ILoggerFactory AddGrandOutput( this ILoggerFactory loggerFactory, GrandOutput grandOutput )
        //{
        //    if( grandOutput == null ) throw new ArgumentNullException( nameof( grandOutput ) );

        //    loggerFactory.AddProvider( new GrandOutputLoggerProvider( grandOutput ) );
        //    return loggerFactory;
        //}
    }
}
