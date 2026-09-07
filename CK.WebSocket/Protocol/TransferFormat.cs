namespace CK.WebSocket;

/// <summary>
/// Represents the possible transfer formats.
/// </summary>
public enum TransferFormat
{
    /// <summary>The message is transferred as binary data.</summary>
    Binary = 1,
    /// <summary>The message is transferred as UTF-8 text.</summary>
    Text = 2
}
