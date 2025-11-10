using System;
using System.Threading;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using AvaloniaMediaPlayer.Core;
using AvaloniaMediaPlayer.Core.Codecs;

namespace JellyTV.Services;

public class MediaPlayerService : IDisposable, IPlayerCallback
{
    private VideoPlayer? _player;
    private DispatcherTimer? _renderTimer;
    private WriteableBitmap? _videoBitmap;
    private readonly object _bitmapLock = new object();

    // Video info properties
    private int _videoWidth;
    private int _videoHeight;

    // Public property for ViewModel to bind to
    public WriteableBitmap? VideoBitmap
    {
        get
        {
            lock (_bitmapLock)
            {
                return _videoBitmap;
            }
        }
    }

    // Media info properties for stats display
    public string? VideoCodec { get; private set; }
    public int VideoWidth => _videoWidth;
    public int VideoHeight => _videoHeight;
    public string? AudioCodec { get; private set; }
    public float Framerate { get; private set; }
    public long Bitrate { get; private set; }

    // Event to notify when a new frame is available
    public event Action? VideoFrameUpdated;

    public MediaPlayerService(string hardwareAcceleration = "Auto")
    {
        Console.WriteLine($"[DEPRECATED] MediaPlayerService - VideoPlayerControl now handles all playback");
        // This service is no longer used - VideoPlayerControl handles everything
        // Keeping minimal initialization to avoid breaking existing references
    }

    public async void PlayMedia(string url)
    {
        try
        {
            Console.WriteLine($"Starting playback: {url}");

            // Stop any currently playing media
            StopPlayback();

            // Create new player instance
            _player = new VideoPlayer(this);

            var options = new PlayerOptions
            {
                StartTime = 0,
                VideoOnly = false
            };

            // Open the media file
            var success = await _player.OpenFileAsync(url, options);
            if (success)
            {
                // Get video stream info
                if (_player.GetVideoStreamCount() > 0)
                {
                    _player.GetVideoStreamInfo(0, out var videoInfo);
                    VideoCodec = videoInfo.Codec;
                    _videoWidth = videoInfo.Width;
                    _videoHeight = videoInfo.Height;
                    Framerate = (float)videoInfo.FrameRate;
                    Bitrate = videoInfo.Bitrate;
                    Console.WriteLine($"Video: {_videoWidth}x{_videoHeight}, {Framerate}fps, Codec: {VideoCodec}");
                }

                // Get audio stream info
                if (_player.GetAudioStreamCount() > 0)
                {
                    _player.GetAudioStreamInfo(0, out var audioInfo);
                    AudioCodec = audioInfo.Codec;
                    Console.WriteLine($"Audio Codec: {AudioCodec}");
                }

                // Start render timer
                _renderTimer?.Start();
                Console.WriteLine("Playback started successfully");
            }
            else
            {
                Console.WriteLine("Failed to open media file");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting playback: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }

    private VideoFrame? _lastRenderedFrame = null;

    private void RenderTimer_Tick(object? sender, EventArgs e)
    {
        if (_player == null || !_player.IsPlaying)
            return;

        // Get current video frame
        var frame = _player.GetCurrentVideoFrame();

        // Only render if we have a new frame (avoid re-rendering same frame)
        if (frame != null && frame != _lastRenderedFrame)
        {
            RenderVideoFrame(frame);
            _lastRenderedFrame = frame;
        }
    }

    private void RenderVideoFrame(VideoFrame frame)
    {
        try
        {
            lock (_bitmapLock)
            {
                // Create or update WriteableBitmap
                if (_videoBitmap == null ||
                    _videoBitmap.PixelSize.Width != frame.Width ||
                    _videoBitmap.PixelSize.Height != frame.Height)
                {
                    _videoBitmap = new WriteableBitmap(
                        new PixelSize(frame.Width, frame.Height),
                        new Vector(96, 96),
                        PixelFormat.Bgra8888,
                        AlphaFormat.Premul);
                }

                using (var buffer = _videoBitmap.Lock())
                {
                    unsafe
                    {
                        var dst = (byte*)buffer.Address;
                        fixed (byte* src = frame.Data)
                        {
                            // Copy frame data to bitmap
                            for (int y = 0; y < frame.Height; y++)
                            {
                                Buffer.MemoryCopy(
                                    src + (y * frame.Stride),
                                    dst + (y * buffer.RowBytes),
                                    buffer.RowBytes,
                                    Math.Min(frame.Stride, buffer.RowBytes));
                            }
                        }
                    }
                }
            }

            // Notify that a new frame is available
            VideoFrameUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error rendering video frame: {ex.Message}");
        }
    }

    public void StopPlayback()
    {
        try
        {
            _renderTimer?.Stop();

            if (_player != null)
            {
                Console.WriteLine("Stopping playback");
                _player.CloseFileAsync().Wait();
                _player.Dispose();
                _player = null;
                Console.WriteLine("Playback stopped");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping playback: {ex.Message}");
        }
    }

    public bool IsPlaying => _player?.IsPlaying ?? false;

    public void Pause()
    {
        try
        {
            if (_player != null && _player.IsPlaying)
            {
                _player.Pause();
                Console.WriteLine("Playback paused");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error pausing playback: {ex.Message}");
        }
    }

    public void Resume()
    {
        try
        {
            if (_player != null && !_player.IsPlaying)
            {
                _player.Resume();
                Console.WriteLine("Playback resumed");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error resuming playback: {ex.Message}");
        }
    }

    public void TogglePlayPause()
    {
        try
        {
            if (_player != null)
            {
                _player.TogglePause();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error toggling play/pause: {ex.Message}");
        }
    }

    public long Position
    {
        get => _player?.GetTime() ?? 0;
        set
        {
            if (_player != null)
            {
                _player.SeekTime(value);
                Console.WriteLine($"Position set to: {value}ms");
            }
        }
    }

    public long Duration => _player?.GetTotalTime() ?? 0;

    public float PlaybackRate => 1.0f; // TODO: Implement if needed

    public void Seek(int seconds)
    {
        try
        {
            if (_player != null)
            {
                var currentTime = _player.GetTime();
                var newTime = Math.Max(0, Math.Min(currentTime + (seconds * 1000), _player.GetTotalTime()));
                _player.SeekTime(newTime);
                Console.WriteLine($"Seeked {seconds} seconds to position: {newTime}ms");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error seeking: {ex.Message}");
        }
    }

    // IPlayerCallback implementation
    public void OnPlayBackStarted()
    {
        Console.WriteLine("Playback started callback");
    }

    public void OnPlayBackEnded()
    {
        Dispatcher.UIThread.Post(() =>
        {
            Console.WriteLine("Playback ended callback");
            _renderTimer?.Stop();
        });
    }

    public void OnPlayBackStopped()
    {
        Console.WriteLine("Playback stopped callback");
    }

    public void OnPlayBackPaused()
    {
        Console.WriteLine("Playback paused callback");
    }

    public void OnPlayBackResumed()
    {
        Console.WriteLine("Playback resumed callback");
    }

    public void OnPlayBackSeek(long timeMs, long offsetMs)
    {
        Console.WriteLine($"Seek callback: {timeMs}ms (offset: {offsetMs}ms)");
    }

    public void OnPlayBackSeekChapter(int chapter)
    {
        Console.WriteLine($"Seek chapter callback: {chapter}");
    }

    public void OnPlayBackSpeedChanged(float speed)
    {
        Console.WriteLine($"Speed changed callback: {speed}x");
    }

    public void OnPlayBackError(string error)
    {
        Console.WriteLine($"Playback error callback: {error}");
    }

    public void OnAVChange()
    {
        Console.WriteLine("AV change callback");
    }

    public void OnAVStarted()
    {
        Console.WriteLine("AV started callback");
    }

    public void Dispose()
    {
        try
        {
            _renderTimer?.Stop();
            _player?.Dispose();
            Console.WriteLine("MediaPlayerService disposed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error disposing MediaPlayerService: {ex.Message}");
        }
    }
}
