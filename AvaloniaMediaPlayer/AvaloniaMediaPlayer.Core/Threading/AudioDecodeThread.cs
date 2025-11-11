using AvaloniaMediaPlayer.Core.Codecs;
using AvaloniaMediaPlayer.Core.Renderers;
using FFmpeg.AutoGen;

namespace AvaloniaMediaPlayer.Core.Threading;

/// <summary>
/// Background thread for decoding audio packets
/// Inspired by Kodi's CVideoPlayerAudio
/// </summary>
public unsafe class AudioDecodeThread : IDisposable
{
    private readonly AudioCodec _codec;
    private readonly AudioOutput _audioOutput;
    private readonly PacketQueue _packetQueue;
    private readonly PlaybackStatistics? _statistics;
    private readonly AudioClock? _audioClock;

    private Thread? _thread;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isRunning;
    private bool _isPaused;
    private readonly object _pauseLock = new();

    // Allow audio to buffer up to 250ms ahead of clock for tight A/V sync
    private const long MaxAudioAheadMs = 250;

    public AudioDecodeThread(AudioCodec codec, AudioOutput audioOutput, PacketQueue packetQueue, AudioClock? audioClock = null, PlaybackStatistics? statistics = null)
    {
        _codec = codec;
        _audioOutput = audioOutput;
        _packetQueue = packetQueue;
        _audioClock = audioClock;
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
            Name = "AudioDecodeThread",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal // Higher priority for smooth audio
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

                // Check if audio output queue is full - apply backpressure
                if (_audioOutput.QueuedSamplesCount >= 15)
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
                        _statistics?.RecordAudioFrameDecoded();

                        // Timing-based throttle: wait if audio is too far ahead of playback clock
                        if (_audioClock != null && frame.PTSMs > 0)
                        {
                            long currentTimeMs = _audioClock.GetTime();
                            long audioAheadMs = frame.PTSMs - currentTimeMs;

                            // If audio is more than MaxAudioAheadMs ahead, wait
                            while (audioAheadMs > MaxAudioAheadMs && !cancellationToken.IsCancellationRequested)
                            {
                                Thread.Sleep(5);
                                currentTimeMs = _audioClock.GetTime();
                                audioAheadMs = frame.PTSMs - currentTimeMs;
                            }
                        }

                        _audioOutput.QueueAudioFrame(frame);
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
            Console.WriteLine($"AudioDecodeThread error: {ex.Message}");
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
        // Just clear the audio output queue
        _audioOutput.Flush();
    }

    public bool IsRunning => _isRunning;
    public bool IsPaused => _isPaused;

    public void Dispose()
    {
        Stop();
    }
}
