using AvaloniaMediaPlayer.Core.Demux;
using FFmpeg.AutoGen;

namespace AvaloniaMediaPlayer.Core.Threading;

/// <summary>
/// Background thread for reading packets from demuxer
/// Inspired by Kodi's CDVDDemuxClient
/// </summary>
public unsafe class DemuxThread : IDisposable
{
    private readonly FFmpegDemuxer _demuxer;
    private readonly PacketQueue _videoQueue;
    private readonly PacketQueue _audioQueue;
    private readonly PacketQueue _subtitleQueue;

    private Thread? _thread;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isRunning;
    private readonly object _lock = new();

    private int _videoStreamIndex = -1;
    private int _audioStreamIndex = -1;
    private int _subtitleStreamIndex = -1;

    public DemuxThread(FFmpegDemuxer demuxer, int videoQueueSize = 100, int audioQueueSize = 100)
    {
        _demuxer = demuxer;
        _videoQueue = new PacketQueue(videoQueueSize);
        _audioQueue = new PacketQueue(audioQueueSize);
        _subtitleQueue = new PacketQueue(50);
    }

    public void SetStreamIndices(int videoIndex, int audioIndex, int subtitleIndex)
    {
        lock (_lock)
        {
            _videoStreamIndex = videoIndex;
            _audioStreamIndex = audioIndex;
            _subtitleStreamIndex = subtitleIndex;
        }
    }

    public void Start()
    {
        if (_isRunning)
            return;

        _isRunning = true;
        _cancellationTokenSource = new CancellationTokenSource();
        _thread = new Thread(() => DemuxLoop(_cancellationTokenSource.Token))
        {
            Name = "DemuxThread",
            IsBackground = true
        };
        _thread.Start();
    }

    private void DemuxLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _isRunning)
            {
                // Check if any queue is full - apply backpressure
                if (_videoQueue.IsFull || _audioQueue.IsFull)
                {
                    Thread.Sleep(10);
                    continue;
                }

                // Read packet from demuxer
                var packet = _demuxer.ReadPacket();
                if (packet == null)
                {
                    // End of file - signal EOF to all queues
                    _isRunning = false;
                    break;
                }

                // Route packet to appropriate queue based on stream index
                bool enqueued = false;

                lock (_lock)
                {
                    if (packet->stream_index == _videoStreamIndex)
                    {
                        enqueued = _videoQueue.Enqueue(packet, cancellationToken);
                    }
                    else if (packet->stream_index == _audioStreamIndex)
                    {
                        enqueued = _audioQueue.Enqueue(packet, cancellationToken);
                    }
                    else if (packet->stream_index == _subtitleStreamIndex)
                    {
                        enqueued = _subtitleQueue.Enqueue(packet, cancellationToken);
                    }
                }

                // If packet wasn't enqueued (wrong stream or error), free it
                if (!enqueued)
                {
                    _demuxer.FreePacket(packet);
                }

                // Small yield to prevent tight loop
                Thread.Yield();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DemuxThread error: {ex.Message}");
        }
        finally
        {
            _isRunning = false;
        }
    }

    public void Stop()
    {
        _isRunning = false;

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
        _videoQueue.Flush();
        _audioQueue.Flush();
        _subtitleQueue.Flush();
    }

    public PacketQueue VideoQueue => _videoQueue;
    public PacketQueue AudioQueue => _audioQueue;
    public PacketQueue SubtitleQueue => _subtitleQueue;
    public bool IsRunning => _isRunning;

    public void Dispose()
    {
        Stop();
        _videoQueue.Dispose();
        _audioQueue.Dispose();
        _subtitleQueue.Dispose();
    }
}
