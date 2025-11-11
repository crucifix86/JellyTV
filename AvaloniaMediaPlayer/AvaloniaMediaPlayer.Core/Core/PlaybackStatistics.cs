using System.Diagnostics;

namespace AvaloniaMediaPlayer.Core;

/// <summary>
/// Tracks playback statistics for monitoring and debugging
/// </summary>
public class PlaybackStatistics
{
    private readonly Stopwatch _uptime = new();
    private long _videoFramesDecoded;
    private long _videoFramesDropped;
    private long _audioFramesDecoded;
    private long _audioFramesDropped;

    private readonly Stopwatch _fpsMeasureWindow = new();
    private long _framesInWindow;
    private double _currentFPS;

    public PlaybackStatistics()
    {
        _uptime.Start();
        _fpsMeasureWindow.Start();
    }

    // Video stats
    public long VideoFramesDecoded => _videoFramesDecoded;
    public long VideoFramesDropped => _videoFramesDropped;
    public long VideoFramesRendered => _videoFramesDecoded - _videoFramesDropped;

    // Audio stats
    public long AudioFramesDecoded => _audioFramesDecoded;
    public long AudioFramesDropped => _audioFramesDropped;

    // Performance stats
    public double CurrentFPS => _currentFPS;
    public TimeSpan Uptime => _uptime.Elapsed;

    // Queue depths (set externally)
    public int VideoQueueDepth { get; set; }
    public int AudioQueueDepth { get; set; }

    public void RecordVideoFrameDecoded()
    {
        Interlocked.Increment(ref _videoFramesDecoded);
        Interlocked.Increment(ref _framesInWindow);

        // Update FPS every second
        if (_fpsMeasureWindow.ElapsedMilliseconds >= 1000)
        {
            _currentFPS = _framesInWindow * 1000.0 / _fpsMeasureWindow.ElapsedMilliseconds;
            _framesInWindow = 0;
            _fpsMeasureWindow.Restart();
        }
    }

    public void RecordVideoFrameDropped()
    {
        Interlocked.Increment(ref _videoFramesDropped);
    }

    public void RecordAudioFrameDecoded()
    {
        Interlocked.Increment(ref _audioFramesDecoded);
    }

    public void RecordAudioFrameDropped()
    {
        Interlocked.Increment(ref _audioFramesDropped);
    }

    public void Reset()
    {
        _videoFramesDecoded = 0;
        _videoFramesDropped = 0;
        _audioFramesDecoded = 0;
        _audioFramesDropped = 0;
        _framesInWindow = 0;
        _currentFPS = 0;
        _uptime.Restart();
        _fpsMeasureWindow.Restart();
    }

    public override string ToString()
    {
        return $"FPS: {CurrentFPS:F1} | Video: {VideoFramesRendered}/{VideoFramesDecoded} (dropped: {VideoFramesDropped}) | " +
               $"Audio: {AudioFramesDecoded} (dropped: {AudioFramesDropped}) | " +
               $"Queues: V{VideoQueueDepth}/A{AudioQueueDepth} | Uptime: {Uptime:hh\\:mm\\:ss}";
    }
}
