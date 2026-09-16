using CK.AspNet.WebSocket;
using System.Buffers;
using System.Text;

namespace CK.AspNet.WebSocket.Tests;

/// <summary>
/// Minimal delimited protocol for tests: a message is the UTF-8 text of one whole
/// end-of-message-delimited payload.
/// </summary>
sealed class TextProtocol : IDelimitedMessageProtocol<string, string>
{
    public string ParseMessage( ref ReadOnlySequence<byte> input ) => Encoding.UTF8.GetString( input );

    public void WriteMessage( string message, IBufferWriter<byte> output ) => output.Write( Encoding.UTF8.GetBytes( message ) );
}
