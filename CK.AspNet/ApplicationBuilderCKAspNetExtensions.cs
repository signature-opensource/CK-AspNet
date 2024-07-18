using CK.AspNet;
using CK.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.Builder
{
    /// <summary>
    /// Provides the <see cref="AddApplicationBuilder(WebApplicationBuilder, Action{IApplicationBuilder})"/>
    /// and <see cref="CKBuild(WebApplicationBuilder)"/> extension methods.
    /// </summary>
    public static class ApplicationBuilderCKAspNetExtensions
    {
        sealed class PrePipelineBuilder : List<Action<IApplicationBuilder>> { }
        sealed class PipelineBuilder : List<Action<IApplicationBuilder>> { }

        /// <summary>
        /// Adds an action that will be executed by <see cref="CKBuild(WebApplicationBuilder, IStObjMap?)"/> on the built <see cref="WebApplication"/>.
        /// </summary>
        /// <param name="builder">This builder.</param>
        /// <param name="configure">Configuration action.</param>
        /// <returns>This builder.</returns>
        public static WebApplicationBuilder AppendApplicationBuilder( this WebApplicationBuilder builder, Action<IApplicationBuilder> configure )
        {
            Throw.CheckNotNullArgument( configure );
            IDictionary<object, object> props = ((IHostApplicationBuilder)builder).Properties;
            if( props.TryGetValue( typeof( PipelineBuilder ), out var b ) )
            {
                ((PipelineBuilder)b).Add( configure );
            }
            else
            {
                props.Add( typeof( PipelineBuilder ), new PipelineBuilder { configure } );
            }
            return builder;
        }

        /// <summary>
        /// Adds an action that will be executed by <see cref="CKBuild(WebApplicationBuilder, IStObjMap?)"/> on the built <see cref="WebApplication"/>
        /// before any other builders.
        /// <para>
        /// <see cref="AppendApplicationBuilder(WebApplicationBuilder, Action{IApplicationBuilder})"/>
        /// should almost always be used instead of this (the first middleware by default handles <see cref="ScopedHttpContext"/> and
        /// logs request errors (see <see cref="CKBuild(WebApplicationBuilder)"/>).
        /// </para>
        /// </summary>
        /// <param name="builder">This builder.</param>
        /// <param name="configure">Configuration action.</param>
        /// <returns>This builder.</returns>
        public static WebApplicationBuilder PrependApplicationBuilder( this WebApplicationBuilder builder, Action<IApplicationBuilder> configure )
        {
            Throw.CheckNotNullArgument( configure );
            IDictionary<object, object> props = ((IHostApplicationBuilder)builder).Properties;
            if( props.TryGetValue( typeof( PrePipelineBuilder ), out var b ) )
            {
                ((PrePipelineBuilder)b).Add( configure );
            }
            else
            {
                props.Add( typeof( PrePipelineBuilder ), new PrePipelineBuilder { configure } );
            }
            return builder;
        }

        /// <summary>
        /// Wraps the <see cref="WebApplicationBuilder.Build"/>.
        /// <list type="number">
        ///     <item>The <see cref="ScopedHttpContext"/> is added to the <see cref="WebApplicationBuilder.Services"/>.</item>
        ///     <item>If the <paramref name="map"/> is provided, registers it into the services.</item>
        ///     <item><see cref="WebApplicationBuilder.Build"/> is called to obtain the <see cref="WebApplication"/>.</item>
        ///     <item>
        ///      Executes the configurations registered by <see cref="PrependApplicationBuilder(WebApplicationBuilder, Action{IApplicationBuilder})"/> (should rarely be used).
        ///      </item>
        ///     <item>The first default middleware is registered:
        ///         <list type="bullet">
        ///             <item>It handles the <see cref="ScopedHttpContext"/>;</item>
        ///             <item>
        ///             and logs any error in the pipeline into the <see cref="IActivityMonitor"/> if it is available in
        ///             the <see cref="WebApplication.Services"/>.
        ///             </item>
        ///         </list>   
        ///      </item>
        ///      <item>
        ///      Executes the configurations registered by <see cref="AppendApplicationBuilder(WebApplicationBuilder, Action{IApplicationBuilder})"/>.
        ///      </item>
        /// </list>
        /// </summary>
        /// <param name="builder">This builder.</param>
        /// <param name="map">Optional CKomposable map to register.</param>
        /// <returns>The web application.</returns>
        public static WebApplication CKBuild( this WebApplicationBuilder builder, IStObjMap? map = null )
        {
            builder.Services.AddScoped<ScopedHttpContext>();
            builder.ApplyAuto();
            if( map != null )
            {
                builder.Services.AddStObjMap( builder.GetBuilderMonitor(), map );
            }
            var app = builder.Build();
            IDictionary<object, object> props = ((IHostApplicationBuilder)builder).Properties;
            if( props.TryGetValue( typeof( PrePipelineBuilder ), out var first ) )
            {
                PrePipelineBuilder list = (PrePipelineBuilder)first;
                for( int i = list.Count - 1; i >= 0; i-- )
                {
                    list[i]( app );
                }
                props.Remove( typeof( PrePipelineBuilder ) );
            }
            app.UseMiddleware<CKMiddleware>();
            if( props.TryGetValue( typeof( PipelineBuilder ), out var regular ) )
            {
                foreach( var a in (PipelineBuilder)regular ) a( app );
                props.Remove( typeof( PipelineBuilder ) );
            }
            return app;
        }

    }

}
