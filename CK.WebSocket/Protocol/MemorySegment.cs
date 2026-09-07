using System;
using System.Buffers;

namespace CK.WebSocket;

/// <summary>A <see cref="ReadOnlySequenceSegment{T}"/> backed by a single memory block.</summary>
public class MemorySegment<T> : ReadOnlySequenceSegment<T>
{
    /// <summary>Initializes a new segment wrapping the given memory block.</summary>
    public MemorySegment(ReadOnlyMemory<T> memory)
    {
        Memory = memory;
    }

    /// <summary>Appends a new segment holding the given memory block after this one.</summary>
    public MemorySegment<T> Append(ReadOnlyMemory<T> memory)
    {
        var segment = new MemorySegment<T>(memory)
        {
            RunningIndex = RunningIndex + Memory.Length
        };

        Next = segment;

        return segment;
    }
}
