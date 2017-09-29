using CK.Core;
using CK.Monitoring;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CK.Monitoring
{
    internal class GrandOutputDefaultConfigurationInitializer
    {
        readonly IConfigurationSection _section;
        readonly GrandOutput _target;
        IDisposable _changeToken;
        bool _trackUnhandledException;

#if NET461
        IDisposable _listenerSubscription;
        ConcurrentBag<IDisposable> _subscriptions;

        class LogObserver<T> : IObserver<T>
        {
            Action<T> _callback;
            public LogObserver( Action<T> callback ) { _callback = callback; }
            public void OnCompleted() { }
            public void OnError( Exception error ) { }
            public void OnNext( T value ) => _callback( value );
        }

#endif
        public GrandOutputDefaultConfigurationInitializer(
            IHostingEnvironment env,
            IConfigurationSection section,
            GrandOutput target )
        {
            _section = section;
            if( target == null )
            {
                target = GrandOutput.EnsureActiveDefault( new GrandOutputConfiguration() );
                if( LogFile.RootLogPath == null )
                {
                    LogFile.RootLogPath = Path.GetFullPath( Path.Combine( env.ContentRootPath, _section["LogPath"] ?? "Logs" ) );
                }
            }
            _target = target;
            // We do not handle CancellationTokenRegistration.Dispose here.
            // The target is disposing: everything will be discarded, included
            // this instance of initializer.
            _target.DisposingToken.Register( () =>
            {
                _changeToken.Dispose();
                ConfigureGlobalListeners( false, false );
            } );
            ApplyDynamicConfiguration();
            var reloadToken = _section.GetReloadToken();
            _changeToken = reloadToken.RegisterChangeCallback( OnConfigurationChanged, this );
        }

        void ConfigureGlobalListeners( bool trackUnhandledException, bool net461DiagnosticTrace )
        {
            if( trackUnhandledException != _trackUnhandledException )
            {
                if( trackUnhandledException )
                {
                    AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                    TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
                }
                else
                {
                    AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
                    TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
                }
                _trackUnhandledException = trackUnhandledException;
            }
#if NET461
            if( net461DiagnosticTrace != (_listenerSubscription != null) )
            {
                if( net461DiagnosticTrace )
                {
                    _subscriptions = new ConcurrentBag<IDisposable>();
                    _listenerSubscription = DiagnosticListener.AllListeners.Subscribe( new LogObserver<DiagnosticListener>( listener =>
                    {
                        var s = listener.Subscribe( new LogObserver<KeyValuePair<string, object>>( diagnosticEvent =>
                        {
                            _target.ExternalLog( CK.Core.LogLevel.Info, $"{diagnosticEvent.Key}={diagnosticEvent.Value}" );
                        } ) );
                        _subscriptions.Add( s );
                    } ) );
                }
                else
                {
                    _listenerSubscription.Dispose();
                    _listenerSubscription = null;
                    foreach( var s in _subscriptions ) s.Dispose();
                    _subscriptions = null;
                }
            }
#endif
        }

        void ApplyDynamicConfiguration()
        {
            bool trackUnhandledException = !String.Equals( _section["LogUnhandledExceptions"], "false", StringComparison.OrdinalIgnoreCase );
            bool net461DiagnosticTrace = !String.Equals( _section["HandleDiagnosticsEvents"], "false", StringComparison.OrdinalIgnoreCase );
            ConfigureGlobalListeners( trackUnhandledException, net461DiagnosticTrace );
            var c = new GrandOutputConfiguration();
            var gSection = _section.GetSection( "GrandOutput" );
            if( gSection.Exists() )
            {
                gSection.Bind( c );
                var hSection = gSection.GetSection( "Handlers" );
                foreach( var hConfig in hSection.GetChildren() )
                {
                    Type resolved = TryResolveType( hConfig.Key );
                    if( resolved == null )
                    {
                        ActivityMonitor.CriticalErrorCollector.Add( new CKException( $"Unable to resolve type '{hConfig.Key}'." ), nameof(GrandOutputDefaultConfigurationInitializer) );
                        continue;
                    }
                    try
                    {
                        var config = Activator.CreateInstance( resolved );
                        hConfig.Bind( config );
                        c.AddHandler( (IHandlerConfiguration)config );
                    }
                    catch( Exception ex )
                    {
                        ActivityMonitor.CriticalErrorCollector.Add( ex, nameof( GrandOutputDefaultConfigurationInitializer ) );
                    }
                }
            }
            _target.ApplyConfiguration( c );
        }

        void OnUnobservedTaskException( object sender, UnobservedTaskExceptionEventArgs e )
        {
            ActivityMonitor.CriticalErrorCollector.Add( e.Exception, "UnobservedTaskException" );
            e.SetObserved();
        }

        void OnUnhandledException( object sender, UnhandledExceptionEventArgs e )
        {
            var ex = e.ExceptionObject as Exception;
            if( ex != null ) ActivityMonitor.CriticalErrorCollector.Add( ex, "UnhandledException" );
            else
            {
                string errText = e.ExceptionObject.ToString();
                _target.ExternalLog( LogLevel.Fatal, errText, GrandOutput.CriticalErrorTag );
            }
        }

        Type TryResolveType( string name )
        {
            Type resolved;
            if( name.IndexOf( ',' ) >= 0 )
            {
                // It must be an assembly qualified name.
                // Weaken its name and tty to load it.
                // If it fails and the name does not end with "Configuration" tries it.
                string fullTypeName, assemblyFullName, assemblyName, versionCultureAndPublicKeyToken;
                if( SimpleTypeFinder.SplitAssemblyQualifiedName( name, out fullTypeName, out assemblyFullName )
                    && SimpleTypeFinder.SplitAssemblyFullName( assemblyFullName, out assemblyName, out versionCultureAndPublicKeyToken ) )
                {
                    var weakTypeName = fullTypeName + ", " + assemblyName;
                    resolved = SimpleTypeFinder.RawGetType( weakTypeName, false );
                    if( resolved != null ) return IsHandlerConfiguration( resolved );
                    if( !fullTypeName.EndsWith( "Configuration" ) )
                    {
                        weakTypeName = fullTypeName + "Configuration, " + assemblyName;
                        resolved = SimpleTypeFinder.RawGetType( weakTypeName, false );
                        if( resolved != null ) return IsHandlerConfiguration( resolved );
                    }
                }
                return null;
            }
            // This is a simple type name: try to find the type name in already loaded assemblies.
            var configTypes = AppDomain.CurrentDomain.GetAssemblies()
                                .SelectMany( a => a.GetTypes() )
                                .Where( t => typeof( IHandlerConfiguration ).IsAssignableFrom( t ) )
                                .ToList();
            var nameWithC = !name.EndsWith( "Configuration" ) ? name + "Configuration" : null;
            if( name.IndexOf('.') > 0 )
            {
                // It is a FullName.
                resolved = configTypes.FirstOrDefault( t => t.FullName == name
                                                            || (nameWithC != null && t.FullName == nameWithC) );
            }
            else
            {
                // There is no dot in the name.
                resolved = configTypes.FirstOrDefault( t => t.Name == name
                                                            || (nameWithC != null && t.Name == nameWithC) );
            }
            return resolved;
        }

        Type IsHandlerConfiguration( Type candidate )
        {
            if( typeof( IHandlerConfiguration ).IsAssignableFrom( candidate ) ) return candidate;
            return null;
        }

        static void OnConfigurationChanged( object obj )
        {
            Debug.Assert( obj is GrandOutputDefaultConfigurationInitializer );
            var initializer = (GrandOutputDefaultConfigurationInitializer)obj;
            initializer.ApplyDynamicConfiguration();
            initializer.RenewChangeToken();
        }

        void RenewChangeToken()
        {
            // Disposes the previous change token.
            _changeToken.Dispose();
            // Reacquires the token: using this as the state keeps this object alive.
            var reloadToken = _section.GetReloadToken();
            _changeToken = reloadToken.RegisterChangeCallback( OnConfigurationChanged, this );
        }
    }
}
