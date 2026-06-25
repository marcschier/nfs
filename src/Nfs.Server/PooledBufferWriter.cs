using System.Buffers;

namespace Nfs.Server;

internal sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
{
    private byte[] _buffer;
    private int _written;
    private bool _disposed;

    public PooledBufferWriter(int initialCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 1));
    }

    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _written = 0;
    }

    public void Advance(int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (_written > _buffer.Length - count)
        {
            throw new InvalidOperationException("Cannot advance beyond the end of the buffer.");
        }

        _written += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(_written);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        byte[] buffer = _buffer;
        _buffer = [];
        ArrayPool<byte>.Shared.Return(buffer);
    }

    private void EnsureCapacity(int sizeHint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        if (sizeHint == 0)
        {
            sizeHint = 1;
        }

        if (sizeHint <= _buffer.Length - _written)
        {
            return;
        }

        int newSize = Math.Max(_buffer.Length * 2, _written + sizeHint);
        byte[] newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
        _buffer.AsSpan(0, _written).CopyTo(newBuffer);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = newBuffer;
    }
}
