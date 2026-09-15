using CK.AspNet.WebSocket;
using System;
using System.Buffers;
using System.Text;

namespace CK.AspNet.WebSocket.Tests;

/// <summary>
/// Test protocol that behaves like <see cref="TextProtocol"/>, except it throws while parsing the
/// sentinel payload <see cref="Poison"/>. It drives the dispatcher's OnParsingIssueAsync path, which no
/// real protocol of the channel triggers (RawMessageProtocol never throws). The throw is selective so a
/// normal message sent right after still round-trips: this proves the connection survives a parse error.
/// </summary>
sealed class ThrowingTextProtocol : IDelimitedMessageProtocol<string, string>
{
    /// <summary>The one payload that makes <see cref="ParseMessage"/> throw.</summary>
    public const string Poison = "boom";

    public string ParseMessage( ref ReadOnlySequence<byte> input )
    {
        var text = Encoding.UTF8.GetString( input );
        if( text == Poison ) throw new InvalidOperationException( "Deliberate parse failure." );
        return text;
    }

    public void WriteMessage( string message, IBufferWriter<byte> output ) => output.Write( Encoding.UTF8.GetBytes( message ) );
}
