using AvaloniaMediaPlayer.Core.Codecs;
using AvaloniaMediaPlayer.Core.Demux;
using AvaloniaMediaPlayer.Core.Overlay;
using AvaloniaMediaPlayer.Core.Renderers;
using AvaloniaMediaPlayer.Core.Threading;
using FFmpeg.AutoGen;
using System.Diagnostics;

namespace AvaloniaMediaPlayer.Core;

/// <summary>
/// Main video player implementation (inspired by Kodi's CVideoPlayer)
/// Combines demuxer, codecs, and renderers to provide complete playback functionality
/// </summary>
public class VideoPlayer : IPlayer
{
    private readonly IPlayerCallback _callback;
    private readonly FFmpegDemuxer _demuxer;
    private readonly VideoCodec _videoCodec;
    private readonly AudioCodec _audioCodec;
    private readonly DVDOverlayContainer _overlayContainer;
    private readonly VideoRenderer _videoRenderer;
    private readonly AudioOutput _audioOutput;

    private DemuxThread? _demuxThread;
    private VideoDecodeThread? _videoDecodeThread;
    private AudioDecodeThread? _audioDecodeThread;

    private Thread? _clockThread;
    private CancellationTokenSource? _cancellationTokenSource;

    private bool _isPlaying;
    private bool _isPaused;
    private long _currentTimeMs;
    private long _totalTimeMs;
    private float _playbackSpeed = 1.0f;
    private float _volume = 1.0f;
    private bool _isMuted;

    private int _currentVideoStream = -1;
    private int _currentAudioStream = -1;
    private int _currentSubtitleStream = -1;
    private bool _subtitlesVisible = true;

    private readonly AudioClock _audioClock = new();
    private long _lastSeekTimeMs;
    private readonly PlaybackStatistics _statistics = new();

    public IPlayerCallback Callback => _callback;

    public VideoPlayer(IPlayerCallback callback)
    {
        _callback = callback;
        _demuxer = new FFmpegDemuxer();
        _videoCodec = new VideoCodec();
        _audioCodec = new AudioCodec();
        _overlayContainer = new DVDOverlayContainer();
        _videoRenderer = new VideoRenderer(_overlayContainer);
        _videoRenderer.SetStatistics(_statistics);
        _audioOutput = new AudioOutput();
    }

    public async Task<bool> OpenFileAsync(string filePath, PlayerOptions? options = null)
    {
        options ??= new PlayerOptions();

        // Close any existing playback
        await CloseFileAsync();

        // Open file with demuxer
        if (!_demuxer.Open(filePath))
        {
            _callback.OnPlayBackError("Failed to open file");
            return false;
        }

        _totalTimeMs = _demuxer.GetDuration();

        // Find and open video stream
        var videoStreams = _demuxer.GetStreamIndices(StreamType.Video);
        if (videoStreams.Count > 0)
        {
            _currentVideoStream = videoStreams[0];
            var videoInfo = _demuxer.GetStream(_currentVideoStream);
            if (videoInfo != null && !_videoCodec.Open(videoInfo))
            {
                _callback.OnPlayBackError("Failed to open video codec");
                await CloseFileAsync();
                return false;
            }
        }

        // Find and open audio stream
        var audioStreams = _demuxer.GetStreamIndices(StreamType.Audio);
        if (audioStreams.Count > 0 && !options.VideoOnly)
        {
            _currentAudioStream = audioStreams[0];
            var audioInfo = _demuxer.GetStream(_currentAudioStream);
            if (audioInfo != null && _audioCodec.Open(audioInfo))
            {
                // Initialize audio output
                _audioOutput.Initialize(audioInfo.SampleRate, audioInfo.Channels);
                _audioOutput.SetVolume(_volume);
                _audioOutput.SetStatistics(_statistics);
            }
        }

        // Start playback from specified position
        if (options.StartTime > 0)
        {
            _demuxer.Seek((long)(options.StartTime * 1000));
            _currentTimeMs = (long)(options.StartTime * 1000);
        }
        else if (options.StartPercent > 0)
        {
            var startTime = (long)((_totalTimeMs * options.StartPercent) / 100.0);
            _demuxer.Seek(startTime);
            _currentTimeMs = startTime;
        }

        // Initialize multi-threaded playback
        _isPlaying = true;
        _isPaused = false;
        _cancellationTokenSource = new CancellationTokenSource();

        // Create demux thread
        _demuxThread = new DemuxThread(_demuxer, videoQueueSize: 100, audioQueueSize: 100);
        _demuxThread.SetStreamIndices(_currentVideoStream, _currentAudioStream, _currentSubtitleStream);

        // Create video decode thread
        if (_currentVideoStream >= 0)
        {
            _videoDecodeThread = new VideoDecodeThread(_videoCodec, _videoRenderer, _demuxThread.VideoQueue, _statistics);
            _videoDecodeThread.Start();
        }

        // Create audio decode thread
        if (_currentAudioStream >= 0)
        {
            _audioDecodeThread = new AudioDecodeThread(_audioCodec, _audioOutput, _demuxThread.AudioQueue, _audioClock, _statistics);
            _audioDecodeThread.Start();
        }

        // Start demux thread last (it feeds the others)
        _demuxThread.Start();

        // Start clock update thread
        _clockThread = new Thread(() => ClockLoop(_cancellationTokenSource.Token))
        {
            Name = "ClockThread",
            IsBackground = true
        };
        _clockThread.Start();

        _audioClock.Start();
        _callback.OnPlayBackStarted();
        _callback.OnAVStarted();

        return true;
    }

    private void ClockLoop(CancellationToken cancellationToken)
    {
        int frameCounter = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested && _isPlaying)
            {
                if (_isPaused)
                {
                    Thread.Sleep(10);
                    continue;
                }

                // Update playback time from audio clock
                _currentTimeMs = _audioClock.GetTime();
                if (_currentTimeMs > _totalTimeMs)
                {
                    _currentTimeMs = _totalTimeMs;
                }

                // Update statistics queue depths
                _statistics.VideoQueueDepth = _videoRenderer.QueuedFrameCount;
                _statistics.AudioQueueDepth = _audioOutput.QueuedSamplesCount;

                // Log statistics every 30 iterations (~1 second)
                frameCounter++;
                if (frameCounter >= 30)
                {
                    Console.WriteLine(_statistics.ToString());
                    frameCounter = 0;
                }

                // Check if demux thread has stopped (EOF)
                if (_demuxThread != null && !_demuxThread.IsRunning)
                {
                    // Wait a bit for decode threads to finish their queues
                    Thread.Sleep(100);

                    // Check if queues are empty
                    if (_videoRenderer.QueuedFrameCount == 0 && _audioOutput.QueuedSamplesCount == 0)
                    {
                        _isPlaying = false;
                        _callback.OnPlayBackEnded();
                        break;
                    }
                }

                Thread.Sleep(33); // ~30Hz update rate
            }
        }
        catch (Exception ex)
        {
            _callback.OnPlayBackError($"Clock error: {ex.Message}");
        }
    }

    public async Task<bool> CloseFileAsync()
    {
        _isPlaying = false;

        // Stop all threads
        _videoDecodeThread?.Stop();
        _audioDecodeThread?.Stop();
        _demuxThread?.Stop();

        if (_cancellationTokenSource != null)
        {
            _cancellationTokenSource.Cancel();
            _clockThread?.Join(1000);
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }

        // Dispose threads
        _videoDecodeThread?.Dispose();
        _audioDecodeThread?.Dispose();
        _demuxThread?.Dispose();

        _videoDecodeThread = null;
        _audioDecodeThread = null;
        _demuxThread = null;

        _audioClock.Stop();

        _videoCodec.Close();
        _audioCodec.Close();
        _demuxer.Close();
        _videoRenderer.Flush();
        _overlayContainer.Clear();
        _statistics.Reset();

        _currentVideoStream = -1;
        _currentAudioStream = -1;
        _currentSubtitleStream = -1;
        _currentTimeMs = 0;

        _callback.OnPlayBackStopped();

        return await Task.FromResult(true);
    }

    public bool IsPlaying => _isPlaying && !_isPaused;
    public bool HasVideo => _currentVideoStream >= 0;
    public bool HasAudio => _currentAudioStream >= 0;
    public bool CanPause => true;
    public bool CanSeek => true;

    public void Pause()
    {
        if (_isPlaying && !_isPaused)
        {
            _isPaused = true;
            _videoDecodeThread?.Pause();
            _audioDecodeThread?.Pause();
            _audioClock.Pause();
            _audioOutput.Pause();
            _callback.OnPlayBackPaused();
        }
    }

    public void Resume()
    {
        if (_isPlaying && _isPaused)
        {
            _isPaused = false;
            _videoDecodeThread?.Resume();
            _audioDecodeThread?.Resume();
            _audioClock.Resume();
            _audioOutput.Resume();
            _callback.OnPlayBackResumed();
        }
    }

    public void TogglePause()
    {
        if (_isPaused)
            Resume();
        else
            Pause();
    }

    public void SeekTime(long timeMs)
    {
        if (!_demuxer.IsOpen)
            return;

        timeMs = Math.Clamp(timeMs, 0, _totalTimeMs);

        // Pause decode threads during seek
        bool wasPaused = _isPaused;
        if (!wasPaused)
        {
            _videoDecodeThread?.Pause();
            _audioDecodeThread?.Pause();
        }

        // Flush all queues
        _demuxThread?.Flush();
        _videoCodec.Flush();
        _audioCodec.Flush();
        _videoRenderer.Flush();
        _audioOutput.Flush();

        // Perform seek
        _demuxer.Seek(timeMs);

        _currentTimeMs = timeMs;
        _lastSeekTimeMs = timeMs;
        _audioClock.Seek(timeMs);

        // Resume decode threads if not paused
        if (!wasPaused)
        {
            _videoDecodeThread?.Resume();
            _audioDecodeThread?.Resume();
        }

        _callback.OnPlayBackSeek(timeMs, 0);
    }

    public void SeekPercentage(float percent)
    {
        var timeMs = (long)((_totalTimeMs * percent) / 100.0f);
        SeekTime(timeMs);
    }

    public void SetSpeed(float speed)
    {
        _playbackSpeed = speed;
        _audioClock.SetSpeed(speed);
        _callback.OnPlayBackSpeedChanged(speed);
    }

    public long GetTime() => _currentTimeMs;
    public long GetTotalTime() => _totalTimeMs;
    public float GetPercentage() => _totalTimeMs > 0 ? (_currentTimeMs * 100.0f) / _totalTimeMs : 0;

    public void SetVolume(float volume)
    {
        _volume = Math.Clamp(volume, 0, 1);
        _audioOutput.SetVolume(_isMuted ? 0 : _volume);
    }

    public void SetMute(bool mute)
    {
        _isMuted = mute;
        _audioOutput.SetVolume(_isMuted ? 0 : _volume);
    }

    // Stream selection methods
    public int GetAudioStreamCount() => _demuxer.GetStreamCount(StreamType.Audio);
    public int GetAudioStream() => _currentAudioStream;

    public void SetAudioStream(int index)
    {
        var audioStreams = _demuxer.GetStreamIndices(StreamType.Audio);
        if (index >= 0 && index < audioStreams.Count)
        {
            _audioCodec.Close();
            _currentAudioStream = audioStreams[index];
            var audioInfo = _demuxer.GetStream(_currentAudioStream);
            if (audioInfo != null)
            {
                _audioCodec.Open(audioInfo);
            }
            _callback.OnAVChange();
        }
    }

    public void GetAudioStreamInfo(int index, out AudioStreamInfo info)
    {
        info = new AudioStreamInfo { Index = index };
        var audioStreams = _demuxer.GetStreamIndices(StreamType.Audio);
        if (index >= 0 && index < audioStreams.Count)
        {
            var streamInfo = _demuxer.GetStream(audioStreams[index]);
            if (streamInfo != null)
            {
                info.Codec = streamInfo.CodecName;
                info.Channels = streamInfo.Channels;
                info.SampleRate = streamInfo.SampleRate;
                info.Bitrate = (int)streamInfo.Bitrate;
                info.Language = streamInfo.Language;
            }
        }
    }

    public int GetVideoStreamCount() => _demuxer.GetStreamCount(StreamType.Video);
    public int GetVideoStream() => _currentVideoStream;

    public void SetVideoStream(int index)
    {
        var videoStreams = _demuxer.GetStreamIndices(StreamType.Video);
        if (index >= 0 && index < videoStreams.Count)
        {
            _videoCodec.Close();
            _videoRenderer.Flush();
            _currentVideoStream = videoStreams[index];
            var videoInfo = _demuxer.GetStream(_currentVideoStream);
            if (videoInfo != null)
            {
                _videoCodec.Open(videoInfo);
            }
            _callback.OnAVChange();
        }
    }

    public void GetVideoStreamInfo(int index, out VideoStreamInfo info)
    {
        info = new VideoStreamInfo { Index = index };
        var videoStreams = _demuxer.GetStreamIndices(StreamType.Video);
        if (index >= 0 && index < videoStreams.Count)
        {
            var streamInfo = _demuxer.GetStream(videoStreams[index]);
            if (streamInfo != null)
            {
                info.Codec = streamInfo.CodecName;
                info.Width = streamInfo.Width;
                info.Height = streamInfo.Height;
                info.FrameRate = streamInfo.FrameRate;
                info.Bitrate = (int)streamInfo.Bitrate;
            }
        }
    }

    // Subtitle methods
    public int GetSubtitleCount() => _demuxer.GetStreamCount(StreamType.Subtitle);
    public int GetSubtitle() => _currentSubtitleStream;
    public void SetSubtitle(int index) { _currentSubtitleStream = index; }
    public void GetSubtitleStreamInfo(int index, out SubtitleStreamInfo info) { info = new SubtitleStreamInfo { Index = index }; }
    public bool GetSubtitleVisible() => _subtitlesVisible;
    public void SetSubtitleVisible(bool visible) { _subtitlesVisible = visible; }
    public void AddSubtitle(string subFilePath) { /* TODO: Implement external subtitle loading */ }

    // Chapter methods (not implemented for basic player)
    public int GetChapterCount() => 0;
    public int GetChapter() => -1;
    public void SeekChapter(int chapter) { }
    public string GetChapterName(int chapter) => string.Empty;

    // State methods
    public string GetPlayerState() => string.Empty;
    public bool SetPlayerState(string state) => false;

    /// <summary>
    /// Get current video frame for rendering
    /// </summary>
    public VideoFrame? GetCurrentVideoFrame()
    {
        return _videoRenderer.GetNextFrame(_currentTimeMs);
    }

    /// <summary>
    /// Get overlay container for subtitle rendering
    /// </summary>
    public DVDOverlayContainer GetOverlayContainer() => _overlayContainer;

    /// <summary>
    /// Get playback statistics for monitoring and debugging
    /// </summary>
    public PlaybackStatistics GetStatistics() => _statistics;

    public void Dispose()
    {
        CloseFileAsync().Wait();
        _videoCodec.Dispose();
        _audioCodec.Dispose();
        _audioOutput.Dispose();
        _demuxer.Dispose();
    }
}
