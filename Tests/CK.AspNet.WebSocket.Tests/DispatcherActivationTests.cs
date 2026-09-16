using CK.Core;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AspNet.WebSocket.Tests;

/// <summary>
/// The generic UseDispatcher&lt;TDispatcher&gt;() overload activates the dispatcher through DI
/// (ActivatorUtilities), unlike the instance overload every other test uses. A constructor dependency
/// that only DI can supply proves the activation ran.
/// </summary>
[TestFixture]
public class DispatcherActivationTests
{
    [Test]
    public async Task the_generic_UseDispatcher_activates_the_dispatcher_through_DI_Async()
    {
        var (app, baseUri) = await ConfigurableHost.StartAsync(
            build: b =>
            {
                b.UseEndOfMessageDelimitedProtocol( new TextProtocol() );
                b.UseDispatcher<PrefixEchoDispatcher>();
            },
            configureServices: services => services.AddSingleton<IEchoPrefix>( new EchoPrefix( "echo:" ) ) );
        await using var _ = app;

        using var client = new ClientWebSocket();
        await client.ConnectAsync( ConfigurableHost.WsUri( baseUri ), CancellationToken.None );

        // The prefix can only be there if ActivatorUtilities built the dispatcher with its IEchoPrefix
        // injected: a parameterless new would have failed, the dispatcher has no such constructor.
        (await EchoServer.EchoAsync( client, "hi" )).ShouldBe( "echo:hi" );
    }
}

/// <summary>A dependency the dispatcher can only receive through DI.</summary>
interface IEchoPrefix
{
    string Value { get; }
}

sealed class EchoPrefix : IEchoPrefix
{
    public EchoPrefix( string value ) => Value = value;

    public string Value { get; }
}

/// <summary>Echoes each message with a prefix taken from an injected <see cref="IEchoPrefix"/>.</summary>
sealed class PrefixEchoDispatcher : IWebSocketMessageDispatcher<string, string>
{
    readonly IEchoPrefix _prefix;

    public PrefixEchoDispatcher( IEchoPrefix prefix )
    {
        _prefix = prefix;
    }

    public Task OnConnectedAsync( IActivityMonitor monitor, IWebSocketConnectionContext<string> connection ) => Task.CompletedTask;

    public Task OnDisconnectedAsync( IActivityMonitor monitor, IWebSocketConnectionContext<string> connection, Exception? exception ) => Task.CompletedTask;

    public Task DispatchMessageAsync( IActivityMonitor monitor, IWebSocketConnectionContext<string> connection, string message )
        => connection.WriteAsync( _prefix.Value + message ).AsTask();

    public Task OnParsingIssueAsync( IActivityMonitor monitor, IWebSocketConnectionContext<string> connection, Exception exception ) => Task.CompletedTask;
}
