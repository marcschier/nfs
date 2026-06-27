#if NETSTANDARD2_0
namespace System.Buffers;

/// <summary>
/// Minimal, faithful port of <c>System.Buffers.ArrayBufferWriter&lt;T&gt;</c>, which the
/// netstandard2.0 surface lacks. Behaviour matches the runtime type (amortised doubling growth,
/// <see cref="WrittenSpan"/>/<see cref="WrittenMemory"/> accessors). Compiled only on
/// netstandard2.0; every other target framework uses the in-box type.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
internal sealed class ArrayBufferWriter<T> : IBufferWriter<T>
{
    private const int DefaultInitialBufferSize = 256;

    private T[] _buffer;

    public ArrayBufferWriter()
    {
        _buffer = Array.Empty<T>();
        WrittenCount = 0;
    }

    public ArrayBufferWriter(int initialCapacity)
    {
        if (initialCapacity <= 0)
        {
            throw new ArgumentException("Initial capacity must be positive.", nameof(initialCapacity));
        }

        _buffer = new T[initialCapacity];
        WrittenCount = 0;
    }

    public ReadOnlyMemory<T> WrittenMemory => _buffer.AsMemory(0, WrittenCount);

    public ReadOnlySpan<T> WrittenSpan => _buffer.AsSpan(0, WrittenCount);

    public int WrittenCount { get; private set; }

    public int Capacity => _buffer.Length;

    public int FreeCapacity => _buffer.Length - WrittenCount;

    public void Clear()
    {
        _buffer.AsSpan(0, WrittenCount).Clear();
        WrittenCount = 0;
    }

    public void Advance(int count)
    {
        if (count < 0)
        {
            throw new ArgumentException("Count must not be negative.", nameof(count));
        }

        if (WrittenCount > _buffer.Length - count)
        {
            throw new InvalidOperationException("Cannot advance past the end of the buffer.");
        }

        WrittenCount += count;
    }

    public Memory<T> GetMemory(int sizeHint = 0)
    {
        CheckAndResizeBuffer(sizeHint);
        return _buffer.AsMemory(WrittenCount);
    }

    public Span<T> GetSpan(int sizeHint = 0)
    {
        CheckAndResizeBuffer(sizeHint);
        return _buffer.AsSpan(WrittenCount);
    }

    private void CheckAndResizeBuffer(int sizeHint)
    {
        if (sizeHint < 0)
        {
            throw new ArgumentException("Size hint must not be negative.", nameof(sizeHint));
        }

        if (sizeHint == 0)
        {
            sizeHint = 1;
        }

        if (sizeHint > FreeCapacity)
        {
            int currentLength = _buffer.Length;
            int growBy = Math.Max(sizeHint, currentLength);
            if (currentLength == 0)
            {
                growBy = Math.Max(growBy, DefaultInitialBufferSize);
            }

            int newSize = currentLength + growBy;
            if ((uint)newSize > int.MaxValue)
            {
                uint needed = (uint)(currentLength - FreeCapacity + sizeHint);
                if (needed > int.MaxValue)
                {
#pragma warning disable CA2201 // Match the runtime ArrayBufferWriter<T>, which throws OutOfMemoryException.
                    throw new OutOfMemoryException();
#pragma warning restore CA2201
                }

                newSize = int.MaxValue;
            }

            Array.Resize(ref _buffer, newSize);
        }
    }
}
#endif
