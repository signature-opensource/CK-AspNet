namespace CK.AspNet.WebSocket;

/// <summary>Constants used to encode and delimit frames on the wire.</summary>
public static class FrameHelpers
{
    /// <summary>Number of bytes used to encode a frame's length prefix.</summary>
    public const int IntegerLengthEncodedByteCount = 4;
    /// <summary>Marker byte indicating the frame is not the last one of a message.</summary>
    public const byte IsNotEndOfMessageByte = 0;
    /// <summary>Marker byte indicating the frame is the last one of a message.</summary>
    public const byte IsEndOfMessageByte = 1;
}
