using System;
using System.Buffers;

namespace CK.WebSocket;

/// <summary>Incrementally builds a <see cref="ReadOnlySequence{T}"/> out of disjoint memory chunks.</summary>
public class ReadOnlySequenceBuilder<T>
{
    private MemorySegment<T>? _firstSegment;
    private MemorySegment<T>? _lastSegment;

    /// <summary>Appends every segment of <paramref name="sequence"/> as its own chunk.</summary>
    public void Append(ReadOnlySequence<T> sequence)
    {
        foreach (var memory in sequence)
        {
            Append(memory);
        }
    }

    /// <summary>Appends <paramref name="memory"/> as a new trailing chunk.</summary>
    public void Append(ReadOnlyMemory<T> memory)
    {
        var segment = new MemorySegment<T>(memory);

        if (_firstSegment == null)
        {
            _firstSegment = _lastSegment = segment;
        }
        else
        {
            _lastSegment = _lastSegment!.Append(segment.Memory);
        }
    }

    /// <summary>Builds the <see cref="ReadOnlySequence{T}"/> spanning all the appended chunks (empty if none were appended).</summary>
    public ReadOnlySequence<T> Build()
    {
        if (_firstSegment == null)
        {
            return ReadOnlySequence<T>.Empty;
        }

        return new ReadOnlySequence<T>(_firstSegment, 0, _lastSegment, _lastSegment!.Memory.Length);
    }
}
