using OpenTK.Audio.OpenAL;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using AvaloniaMediaPlayer.Core.Codecs;

namespace AvaloniaMediaPlayer.Core.Renderers;

/// <summary>
/// Cross-platform audio output using OpenAL
/// </summary>
public class AudioOutput : IDisposable
{
    private ALDevice _device;
    private ALContext _context;
    private int _source;
    private readonly ConcurrentQueue<int> _bufferQueue = new();
    private readonly List<int> _allBuffers = new();
    private readonly int BufferCount = 8;
    private bool _isPlaying;
    private Thread? _feedThread;
    private CancellationTokenSource? _cancellationTokenSource;

    private int _sampleRate;
    private int _channels;
    private ALFormat _format;

    private readonly ConcurrentQueue<AudioFrame> _audioFrameQueue = new();
    private readonly object _lock = new();
    private PlaybackStatistics? _statistics;
    private long _totalSamplesPlayed;
    private readonly object _playbackLock = new();

    public AudioOutput()
    {
        try
        {
            // Open default audio device
            _device = ALC.OpenDevice(null);
            if (_device == ALDevice.Null)
            {
                throw new Exception("Failed to open OpenAL device");
            }

            // Create context
            _context = ALC.CreateContext(_device, (int[])null!);
            ALC.MakeContextCurrent(_context);

            // Generate source
            _source = AL.GenSource();

            // Generate buffers
            for (int i = 0; i < BufferCount; i++)
            {
                int buffer = AL.GenBuffer();
                _allBuffers.Add(buffer);
                _bufferQueue.Enqueue(buffer);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OpenAL initialization failed: {ex.Message}");
            // Fallback: audio will be silent but player won't crash
        }
    }

    public void Initialize(int sampleRate, int channels)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _totalSamplesPlayed = 0;

        // Determine OpenAL format
        _format = channels == 1 ? ALFormat.Mono16 : ALFormat.Stereo16;

        _isPlaying = true;
        _cancellationTokenSource = new CancellationTokenSource();
        _feedThread = new Thread(() => FeedAudioLoop(_cancellationTokenSource.Token));
        _feedThread.Start();
    }

    public void SetStatistics(PlaybackStatistics statistics)
    {
        _statistics = statistics;
    }

    public void QueueAudioFrame(AudioFrame frame)
    {
        if (_isPlaying)
        {
            _audioFrameQueue.Enqueue(frame);

            // Limit queue size to prevent excessive buffering
            // Return dropped frames to buffer pool
            while (_audioFrameQueue.Count > 15)
            {
                if (_audioFrameQueue.TryDequeue(out var droppedFrame))
                {
                    _statistics?.RecordAudioFrameDropped();
                    droppedFrame.ReturnBuffer();
                }
            }
        }
    }

    private void FeedAudioLoop(CancellationToken cancellationToken)
    {
        try
        {
            // Start playback
            AL.SourcePlay(_source);

            while (!cancellationToken.IsCancellationRequested && _isPlaying)
            {
                // Check for processed buffers
                AL.GetSource(_source, ALGetSourcei.BuffersProcessed, out int processedBuffers);

                // Return processed buffers to queue
                while (processedBuffers > 0)
                {
                    int buffer = AL.SourceUnqueueBuffer(_source);
                    _bufferQueue.Enqueue(buffer);
                    processedBuffers--;
                }

                // Feed new audio frame if available
                if (_audioFrameQueue.TryDequeue(out AudioFrame? audioFrame) && _bufferQueue.TryDequeue(out int availableBuffer))
                {
                    // Upload audio data to buffer (only use actualSize portion)
                    AL.BufferData(availableBuffer, _format, audioFrame.Data.AsSpan(0, audioFrame.ActualSize).ToArray(), _sampleRate);

                    // Track total samples played
                    lock (_playbackLock)
                    {
                        _totalSamplesPlayed += audioFrame.SampleCount;
                    }

                    // Return buffer to pool now that we've copied it to OpenAL
                    audioFrame.ReturnBuffer();

                    // Queue buffer
                    AL.SourceQueueBuffer(_source, availableBuffer);

                    // Ensure playback is running
                    AL.GetSource(_source, ALGetSourcei.SourceState, out int state);
                    if ((ALSourceState)state != ALSourceState.Playing)
                    {
                        AL.SourcePlay(_source);
                    }

                    // Short sleep when actively feeding
                    Thread.Sleep(1);
                }
                else
                {
                    // No data available, sleep longer to avoid busy-waiting
                    Thread.Sleep(5);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Audio feed error: {ex.Message}");
        }
    }

    public void Pause()
    {
        if (_source != 0)
        {
            AL.SourcePause(_source);
        }
    }

    public void Resume()
    {
        if (_source != 0)
        {
            AL.SourcePlay(_source);
        }
    }

    public void Stop()
    {
        _isPlaying = false;

        if (_cancellationTokenSource != null)
        {
            _cancellationTokenSource.Cancel();
            _feedThread?.Join(1000);
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }

        if (_source != 0)
        {
            AL.SourceStop(_source);
            AL.GetSource(_source, ALGetSourcei.BuffersQueued, out int queuedBuffers);
            if (queuedBuffers > 0)
            {
                AL.SourceUnqueueBuffers(_source, queuedBuffers);
            }
        }

        // Clear queues and return audio buffers to pool
        while (_audioFrameQueue.TryDequeue(out var frame))
        {
            frame.ReturnBuffer();
        }
        while (_bufferQueue.TryDequeue(out _)) { }

        // Return all buffers to queue
        foreach (var buffer in _allBuffers)
        {
            _bufferQueue.Enqueue(buffer);
        }
    }

    public void SetVolume(float volume)
    {
        if (_source != 0)
        {
            AL.Source(_source, ALSourcef.Gain, Math.Clamp(volume, 0f, 1f));
        }
    }

    public void Flush()
    {
        lock (_playbackLock)
        {
            _totalSamplesPlayed = 0;
        }
        Stop();
        Initialize(_sampleRate, _channels);
    }

    public void Dispose()
    {
        Stop();

        if (_source != 0)
        {
            AL.DeleteSource(_source);
            _source = 0;
        }

        foreach (var buffer in _allBuffers)
        {
            AL.DeleteBuffer(buffer);
        }
        _allBuffers.Clear();

        if (_context != ALContext.Null)
        {
            ALC.MakeContextCurrent(ALContext.Null);
            ALC.DestroyContext(_context);
            _context = ALContext.Null;
        }

        if (_device != ALDevice.Null)
        {
            ALC.CloseDevice(_device);
            _device = ALDevice.Null;
        }
    }

    public int QueuedSamplesCount => _audioFrameQueue.Count;

    /// <summary>
    /// Get current playback position in seconds based on samples played
    /// </summary>
    public double GetPlaybackPosition()
    {
        if (_sampleRate == 0)
            return 0.0;

        lock (_playbackLock)
        {
            return (double)_totalSamplesPlayed / _sampleRate;
        }
    }
}
