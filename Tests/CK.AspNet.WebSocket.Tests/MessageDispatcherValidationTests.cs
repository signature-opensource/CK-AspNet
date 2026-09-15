using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System;

namespace CK.AspNet.WebSocket.Tests;

/// <summary>
/// The configuration guard of the public entry point. UseWebSocketServer builds a
/// <see cref="MessageDispatcherBuilder{TMessageIn, TMessageOut}"/>, then calls its Validate: a protocol
/// and a dispatcher are both required. A missing one must fail fast with a clear message while the
/// pipeline is wired, not with a null reference once a client connects.
/// </summary>
[TestFixture]
public class MessageDispatcherValidationTests
{
    [Test]
    public void UseWebSocketServer_throws_when_no_protocol_is_configured()
    {
        using var app = BuildAppWithWebSocketServices();
        // A dispatcher but no protocol: Validate checks the protocol first, so this is the branch it hits.
        var ex = Should.Throw<InvalidOperationException>(
            () => app.UseWebSocketServer<string, string>( "/ws", b => b.UseDispatcher( new EchoServer.EchoDispatcher() ) ) );
        ex.Message.ShouldContain( "Protocol", "The message must name what is missing." );
    }

    [Test]
    public void UseWebSocketServer_throws_when_no_dispatcher_is_configured()
    {
        using var app = BuildAppWithWebSocketServices();
        // A protocol but no dispatcher: the protocol check passes, so Validate reaches the dispatcher check.
        var ex = Should.Throw<InvalidOperationException>(
            () => app.UseWebSocketServer<string, string>( "/ws", b => b.UseEndOfMessageDelimitedProtocol( new TextProtocol() ) ) );
        ex.Message.ShouldContain( "Dispatcher", "The message must name what is missing." );
    }

    // A built application whose services satisfy UseWebSocketServer's own GetRequiredService, so the
    // failure under test is Validate's, not a missing dependency. The host is never started: Validate
    // runs while the pipeline is configured.
    static WebApplication BuildAppWithWebSocketServices()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddWebSocketServer();
        return builder.Build();
    }
}
