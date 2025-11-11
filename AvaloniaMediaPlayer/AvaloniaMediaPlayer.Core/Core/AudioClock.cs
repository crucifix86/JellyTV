using System.Diagnostics;

namespace AvaloniaMediaPlayer.Core;

/// <summary>
/// Audio clock for A/V synchronization (inspired by Kodi's CAudioClock)
/// </summary>
public class AudioClock
{
    private readonly Stopwatch _clock = new();
    private long _pauseTimeMs;
    private double _playbackSpeed = 1.0;
    private long _baseTimeMs;
    private long _seekOffset;

    public void Start()
    {
        _clock.Start();
    }

    public void Stop()
    {
        _clock.Stop();
    }

    public void Pause()
    {
        if (_clock.IsRunning)
        {
            _pauseTimeMs = _clock.ElapsedMilliseconds;
            _clock.Stop();
        }
    }

    public void Resume()
    {
        if (!_clock.IsRunning)
        {
            _clock.Start();
        }
    }

    public void Seek(long timeMs)
    {
        _baseTimeMs = timeMs;
        _seekOffset = timeMs;
        _clock.Restart();
    }

    public void SetSpeed(double speed)
    {
        _playbackSpeed = speed;
    }

    /// <summary>
    /// Get current playback time in milliseconds
    /// </summary>
    public long GetTime()
    {
        if (!_clock.IsRunning)
            return _baseTimeMs + _pauseTimeMs;

        return _baseTimeMs + (long)(_clock.ElapsedMilliseconds * _playbackSpeed);
    }

    /// <summary>
    /// Get current playback time in seconds (for PTS comparison)
    /// </summary>
    public double GetTimeSeconds()
    {
        return GetTime() / 1000.0;
    }
}
