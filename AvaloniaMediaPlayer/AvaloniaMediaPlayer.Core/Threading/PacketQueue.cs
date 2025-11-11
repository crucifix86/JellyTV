using FFmpeg.AutoGen;
using System.Collections.Concurrent;

namespace AvaloniaMediaPlayer.Core.Threading;

/// <summary>
/// Thread-safe queue for AVPackets with backpressure support
/// Inspired by Kodi's CDVDMessageQueue
/// </summary>
public unsafe class PacketQueue : IDisposable
{
    private readonly ConcurrentQueue<IntPtr> _queue = new();
    private readonly SemaphoreSlim _dataAvailable = new(0);
    private readonly int _maxSize;
    private int _currentSize;
    private bool _isAborted;
    private readonly object _lock = new();

    public PacketQueue(int maxSize = 100)
    {
        _maxSize = maxSize;
    }

    /// <summary>
    /// Enqueue a packet. Blocks if queue is full (backpressure).
    /// </summary>
    public bool Enqueue(AVPacket* packet, CancellationToken cancellationToken = default)
    {
        if (_isAborted || packet == null)
            return false;

        // Wait if queue is full (backpressure)
        while (!_isAborted && _currentSize >= _maxSize)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            Thread.Sleep(5);
        }

        if (_isAborted)
            return false;

        lock (_lock)
        {
            _queue.Enqueue((IntPtr)packet);
            Interlocked.Increment(ref _currentSize);
        }

        _dataAvailable.Release();
        return true;
    }

    /// <summary>
    /// Dequeue a packet. Blocks if queue is empty until data arrives or timeout.
    /// </summary>
    public AVPacket* Dequeue(int timeoutMs = 100, CancellationToken cancellationToken = default)
    {
        if (_isAborted)
            return null;

        // Wait for data with timeout
        if (!_dataAvailable.Wait(timeoutMs, cancellationToken))
            return null;

        if (_isAborted)
            return null;

        lock (_lock)
        {
            if (_queue.TryDequeue(out IntPtr packetPtr))
            {
                Interlocked.Decrement(ref _currentSize);
                return (AVPacket*)packetPtr;
            }
        }

        return null;
    }

    /// <summary>
    /// Try to dequeue without blocking
    /// </summary>
    public bool TryDequeue(out AVPacket* packet)
    {
        packet = null;

        if (_isAborted || _currentSize == 0)
            return false;

        if (_dataAvailable.Wait(0))
        {
            lock (_lock)
            {
                if (_queue.TryDequeue(out IntPtr packetPtr))
                {
                    packet = (AVPacket*)packetPtr;
                    Interlocked.Decrement(ref _currentSize);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Flush all packets and free memory
    /// </summary>
    public void Flush()
    {
        lock (_lock)
        {
            while (_queue.TryDequeue(out IntPtr packetPtr))
            {
                if (packetPtr != IntPtr.Zero)
                {
                    AVPacket* packet = (AVPacket*)packetPtr;
                    ffmpeg.av_packet_free(&packet);
                }
            }
            _currentSize = 0;
        }

        // Drain the semaphore
        while (_dataAvailable.CurrentCount > 0)
        {
            _dataAvailable.Wait(0);
        }
    }

    /// <summary>
    /// Abort the queue, unblocking all waiting threads
    /// </summary>
    public void Abort()
    {
        _isAborted = true;

        // Release all waiting threads
        for (int i = 0; i < 100; i++)
        {
            try { _dataAvailable.Release(); } catch { }
        }
    }

    public int Count => _currentSize;
    public bool IsAborted => _isAborted;
    public bool IsFull => _currentSize >= _maxSize;

    public void Dispose()
    {
        Abort();
        Flush();
        _dataAvailable.Dispose();
    }
}
