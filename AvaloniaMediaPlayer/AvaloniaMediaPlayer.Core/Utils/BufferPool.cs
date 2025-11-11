using System.Collections.Concurrent;

namespace AvaloniaMediaPlayer.Core.Utils;

/// <summary>
/// Object pool for byte arrays to reduce GC pressure
/// Reuses buffers instead of allocating new ones for every frame
/// </summary>
public class BufferPool
{
    private readonly ConcurrentBag<byte[]> _pool = new();
    private readonly int _bufferSize;
    private int _totalAllocated;
    private const int MaxPoolSize = 30; // Keep max 30 buffers (about 1 second at 30fps)

    public BufferPool(int bufferSize)
    {
        _bufferSize = bufferSize;
    }

    /// <summary>
    /// Rent a buffer from the pool or allocate a new one
    /// </summary>
    public byte[] Rent()
    {
        if (_pool.TryTake(out var buffer))
        {
            return buffer;
        }

        _totalAllocated++;
        return new byte[_bufferSize];
    }

    /// <summary>
    /// Return a buffer to the pool for reuse
    /// </summary>
    public void Return(byte[] buffer)
    {
        if (buffer == null || buffer.Length != _bufferSize)
            return;

        // Limit pool size to prevent unbounded growth
        if (_pool.Count < MaxPoolSize)
        {
            _pool.Add(buffer);
        }
    }

    public int PooledCount => _pool.Count;
    public int TotalAllocated => _totalAllocated;
}
