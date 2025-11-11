using AvaloniaMediaPlayer.Core.Codecs;
using AvaloniaMediaPlayer.Core.Renderers;
using FFmpeg.AutoGen;

namespace AvaloniaMediaPlayer.Core.Threading;

/// <summary>
/// Background thread for decoding video packets
/// Inspired by Kodi's CVideoPlayerVideo
/// </summary>
public unsafe class VideoDecodeThread : IDisposable
{
    private readonly VideoCodec _codec;
    private readonly VideoRenderer _renderer;
    private readonly PacketQueue _packetQueue;
    private readonly PlaybackStatistics? _statistics;

    private Thread? _thread;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isRunning;
    private bool _isPaused;
    private readonly object _pauseLock = new();

    public VideoDecodeThread(VideoCodec codec, VideoRenderer renderer, PacketQueue packetQueue, PlaybackStatistics? statistics = null)
    {
        _codec = codec;
        _renderer = renderer;
        _packetQueue = packetQueue;
        _statistics = statistics;
    }

    public void Start()
    {
        if (_isRunning)
            return;

        _isRunning = true;
        _isPaused = false;
        _cancellationTokenSource = new CancellationTokenSource();
        _thread = new Thread(() => DecodeLoop(_cancellationTokenSource.Token))
        {
            Name = "VideoDecodeThread",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal // Higher priority for smooth video
        };
        _thread.Start();
    }

    private void DecodeLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _isRunning)
            {
                // Handle pause
                lock (_pauseLock)
                {
                    while (_isPaused && !cancellationToken.IsCancellationRequested)
                    {
                        Monitor.Wait(_pauseLock, 10);
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                    break;

                // Check if renderer queue is full - apply backpressure
                if (_renderer.QueuedFrameCount >= 10)
                {
                    Thread.Sleep(5);
                    continue;
                }

                // Dequeue packet with timeout
                var packet = _packetQueue.Dequeue(100, cancellationToken);
                if (packet == null)
                {
                    // No packet available, continue waiting
                    continue;
                }

                try
                {
                    // Decode the packet
                    var frame = _codec.DecodeFrame(packet);
                    if (frame != null)
                    {
                        _statistics?.RecordVideoFrameDecoded();
                        _renderer.QueueFrame(frame);
                    }
                }
                finally
                {
                    // Always free the packet
                    ffmpeg.av_packet_free(&packet);
                }

                // Small yield
                Thread.Yield();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"VideoDecodeThread error: {ex.Message}");
        }
        finally
        {
            _isRunning = false;
        }
    }

    public void Pause()
    {
        lock (_pauseLock)
        {
            _isPaused = true;
        }
    }

    public void Resume()
    {
        lock (_pauseLock)
        {
            _isPaused = false;
            Monitor.PulseAll(_pauseLock);
        }
    }

    public void Stop()
    {
        _isRunning = false;

        // Wake up thread if paused
        Resume();

        if (_cancellationTokenSource != null)
        {
            _cancellationTokenSource.Cancel();
            _thread?.Join(1000);
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }
    }

    public void Flush()
    {
        // Codec flush will be handled by VideoPlayer
        // Just clear the renderer queue
        _renderer.Flush();
    }

    public bool IsRunning => _isRunning;
    public bool IsPaused => _isPaused;

    public void Dispose()
    {
        Stop();
    }
}
