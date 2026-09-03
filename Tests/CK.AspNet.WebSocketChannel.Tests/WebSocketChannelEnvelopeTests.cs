using System;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Shouldly;

namespace CK.AspNet.WebSocketChannel.Tests;

/// <summary>
/// The envelope in isolation: what a feature writes is what the client's router receives, untouched.
/// </summary>
[TestFixture]
public class WebSocketChannelEnvelopeTests
{
    static ReadOnlyMemory<byte> Utf8( string s ) => Encoding.UTF8.GetBytes( s );

    [Test]
    public void Create_embeds_the_payload_verbatim_under_the_topic()
    {
        // The very frame CK.Observable.WebSocketWatcher writes for a transaction event.
        const string payload = """["D",{"N":12,"E":[["I",1,"P",0]],"L":11}]""";

        var frame = WebSocketChannelEnvelope.Create( "OD", Utf8( payload ) );

        using var doc = JsonDocument.Parse( frame );
        doc.RootElement.GetProperty( "topic" ).GetString().ShouldBe( "OD" );
        doc.RootElement.GetProperty( "message" ).GetRawText().ShouldBe( payload,
            "The envelope only wraps: the payload is embedded as a JSON value, not escaped." );
    }

    [TestCase( null )]
    [TestCase( "" )]
    [TestCase( "  " )]
    public void Create_rejects_a_null_empty_or_white_space_topic( string? topic )
    {
        // A topic routes the message on the client: without one the frame goes nowhere.
        Should.Throw<ArgumentException>( () => WebSocketChannelEnvelope.Create( topic!, Utf8( "1" ) ) );
    }
}
