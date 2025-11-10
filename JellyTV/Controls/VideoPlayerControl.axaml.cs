using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Controls.Primitives;
using AvaloniaMediaPlayer.Core;
using AvaloniaMediaPlayer.Core.Codecs;
using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace JellyTV.Controls;

public partial class VideoPlayerControl : UserControl, INotifyPropertyChanged, IPlayerCallback
{
    private VideoPlayer? _player;
    private Image? _videoImage;
    private Button? _playPauseButton;
    private ProgressBar? _progressBar;
    private TextBlock? _subtitleText;
    private Grid? _controlsOverlay;
    private DispatcherTimer? _renderTimer;
    private DispatcherTimer? _hideControlsTimer;
    private WriteableBitmap? _videoBitmap;

    private bool _showControls = false;
    private double _progressPercent;
    private string _currentTimeFormatted = "00:00";
    private string _totalTimeFormatted = "00:00";

    public VideoPlayerControl()
    {
        InitializeComponent();
        DataContext = this;

        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS for smoother playback
        };
        _renderTimer.Tick += RenderTimer_Tick;

        _hideControlsTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _hideControlsTimer.Tick += (s, e) =>
        {
            ShowControls = false;
            _hideControlsTimer.Stop();
        };

        PointerMoved += (s, e) =>
        {
            // Only show controls if we're actually playing
            if (_player != null && _player.IsPlaying)
            {
                ShowControls = true;
                _hideControlsTimer.Stop();
                _hideControlsTimer.Start();
            }
        };

        // Show controls when any key is pressed (for gamepad navigation)
        KeyDown += (s, e) =>
        {
            if (_player != null && _player.IsPlaying)
            {
                ShowControls = true;
                _hideControlsTimer.Stop();
                _hideControlsTimer.Start();
                Console.WriteLine("VideoPlayerControl: Key pressed, showing controls");
            }
        };

        // Handle control unload to properly dispose player
        Unloaded += (s, e) =>
        {
            CleanupPlayer();
        };

        // Handle visibility changes to stop playback when hidden and focus when shown
        this.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(IsVisible))
            {
                Console.WriteLine($"VideoPlayerControl IsVisible changed to: {IsVisible}");
                if (!IsVisible)
                {
                    Console.WriteLine("VideoPlayerControl hidden - cleaning up player");
                    CleanupPlayer();
                }
                else
                {
                    // When VideoPlayerControl becomes visible, grab focus so gamepad input works
                    Dispatcher.UIThread.Post(() =>
                    {
                        this.Focus();
                        Console.WriteLine("VideoPlayerControl grabbed focus");
                    }, DispatcherPriority.Loaded);
                }
            }
        };
    }

    private void CleanupPlayer()
    {
        Console.WriteLine("CleanupPlayer called");
        _renderTimer?.Stop();
        _hideControlsTimer?.Stop();

        if (_player != null)
        {
            Console.WriteLine("Disposing video player");
            _player.Dispose();
            _player = null;
        }

        // Clear the video image
        if (_videoImage != null)
        {
            _videoImage.Source = null;
            Console.WriteLine("Cleared video image");
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        _videoImage = this.FindControl<Image>("VideoImage");
        _playPauseButton = this.FindControl<Button>("PlayPauseButton");
        _progressBar = this.FindControl<ProgressBar>("ProgressBar");
        _subtitleText = this.FindControl<TextBlock>("SubtitleText");
        _controlsOverlay = this.FindControl<Grid>("ControlsOverlay");

        if (_playPauseButton != null)
        {
            _playPauseButton.Click += (s, e) =>
            {
                if (_player != null)
                {
                    _player.TogglePause();
                    UpdatePlayPauseButton();
                }
            };
        }

        if (_progressBar != null)
        {
            _progressBar.PointerPressed += (s, e) =>
            {
                if (_player != null && _progressBar != null)
                {
                    var pos = e.GetPosition(_progressBar);
                    var percent = (pos.X / _progressBar.Bounds.Width) * 100;
                    _player.SeekPercentage((float)percent);
                }
            };
        }

        // Subtitle toggle button
        var subtitleButton = this.FindControl<Button>("SubtitleButton");
        if (subtitleButton != null)
        {
            subtitleButton.Click += (s, e) =>
            {
                if (_player != null)
                {
                    bool visible = _player.GetSubtitleVisible();
                    _player.SetSubtitleVisible(!visible);
                    Console.WriteLine($"Subtitles {(!visible ? "enabled" : "disabled")}");
                }
            };
        }

        // Volume slider
        var volumeSlider = this.FindControl<Slider>("VolumeSlider");
        if (volumeSlider != null)
        {
            volumeSlider.PropertyChanged += (s, e) =>
            {
                if (e.Property.Name == "Value" && _player != null)
                {
                    _player.SetVolume((float)(volumeSlider.Value / 100.0));
                }
            };
        }

        // Volume button - toggle mute
        var volumeButton = this.FindControl<Button>("VolumeButton");
        if (volumeButton != null)
        {
            volumeButton.Click += (s, e) =>
            {
                if (_player != null)
                {
                    _player.SetMute(!_player.GetSubtitleVisible()); // TODO: need to track mute state properly
                    volumeButton.Content = _player.GetSubtitleVisible() ? "🔊" : "🔇";
                }
            };
        }

        // Settings button - cycle playback speed
        var settingsButton = this.FindControl<Button>("SettingsButton");
        if (settingsButton != null)
        {
            int speedIndex = 3; // Start at Normal (1.0x)
            var speedValues = new[] { 0.25f, 0.5f, 0.75f, 1.0f, 1.25f, 1.5f, 2.0f };
            var speedNames = new[] { "0.25x", "0.5x", "0.75x", "1x", "1.25x", "1.5x", "2x" };

            settingsButton.Click += (s, e) =>
            {
                if (_player == null) return;

                speedIndex = (speedIndex + 1) % speedValues.Length;
                _player.SetSpeed(speedValues[speedIndex]);
                Console.WriteLine($"Playback speed: {speedNames[speedIndex]}");
            };
        }

        // Fullscreen button
        var fullscreenButton = this.FindControl<Button>("FullscreenButton");
        if (fullscreenButton != null)
        {
            fullscreenButton.Click += (s, e) =>
            {
                var window = TopLevel.GetTopLevel(this) as Window;
                if (window != null)
                {
                    window.WindowState = window.WindowState == WindowState.FullScreen
                        ? WindowState.Normal
                        : WindowState.FullScreen;
                }
            };
        }
    }

    public async void OpenFile(string filePath)
    {
        _player?.Dispose();
        _player = new VideoPlayer(this);

        var options = new PlayerOptions
        {
            StartTime = 0,
            VideoOnly = false
        };

        var success = await _player.OpenFileAsync(filePath, options);
        if (success)
        {
            ShowControls = true; // Show controls initially
            _renderTimer?.Start();
            _hideControlsTimer?.Start();
            Console.WriteLine("Video opened successfully, controls visible");
        }
    }

    // Public methods for ViewModel to control playback
    public void TogglePause()
    {
        _player?.TogglePause();
        UpdatePlayPauseButton();
    }

    public void Stop()
    {
        Console.WriteLine("VideoPlayerControl.Stop() called");
        CleanupPlayer();
    }

    public void Seek(int seconds)
    {
        if (_player != null)
        {
            var currentTime = _player.GetTime();
            var newTime = Math.Max(0, Math.Min(currentTime + (seconds * 1000), _player.GetTotalTime()));
            _player.SeekTime(newTime);
        }
    }

    public long GetPosition() => _player?.GetTime() ?? 0;
    public long GetDuration() => _player?.GetTotalTime() ?? 0;
    public bool IsPlayerPlaying => _player?.IsPlaying ?? false;

    private void RenderTimer_Tick(object? sender, EventArgs e)
    {
        if (_player == null || !_player.IsPlaying)
            return;

        // Update time display
        var currentMs = _player.GetTime();
        var totalMs = _player.GetTotalTime();

        // Get next video frame based on current playback time
        var frame = _player.GetCurrentVideoFrame();
        if (frame != null)
        {
            RenderVideoFrame(frame);
        }
        else
        {
            // Log when we don't have a frame - indicates starvation
            Console.WriteLine($"[RENDER] No frame available at {currentMs}ms");
        }

        CurrentTimeFormatted = FormatTime(currentMs);
        TotalTimeFormatted = FormatTime(totalMs);
        ProgressPercent = totalMs > 0 ? (currentMs * 100.0) / totalMs : 0;

        // Update subtitle display
        var overlays = _player.GetOverlayContainer().GetOverlays(currentMs / 1000.0);
        var textOverlay = overlays.FirstOrDefault(o => o is AvaloniaMediaPlayer.Core.Overlay.DVDOverlayText) as AvaloniaMediaPlayer.Core.Overlay.DVDOverlayText;
        if (_subtitleText != null)
        {
            _subtitleText.Text = textOverlay?.Text ?? "";
            _subtitleText.IsVisible = !string.IsNullOrEmpty(textOverlay?.Text);
        }
    }

    private void RenderVideoFrame(VideoFrame frame)
    {
        if (_videoImage == null)
            return;

        // Create or update WriteableBitmap
        if (_videoBitmap == null || _videoBitmap.PixelSize.Width != frame.Width || _videoBitmap.PixelSize.Height != frame.Height)
        {
            _videoBitmap = new WriteableBitmap(
                new PixelSize(frame.Width, frame.Height),
                new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888);

            Console.WriteLine($"Created bitmap {frame.Width}x{frame.Height}, Image bounds: {_videoImage.Bounds}, Parent bounds: {this.Bounds}");
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

        _videoImage.Source = _videoBitmap;
        _videoImage.InvalidateVisual();
    }

    private string FormatTime(long milliseconds)
    {
        var span = TimeSpan.FromMilliseconds(milliseconds);
        return span.Hours > 0
            ? span.ToString(@"h\:mm\:ss")
            : span.ToString(@"mm\:ss");
    }

    private void UpdatePlayPauseButton()
    {
        if (_playPauseButton != null && _player != null)
        {
            _playPauseButton.Content = _player.IsPlaying ? "⏸" : "▶";
        }
    }

    // IPlayerCallback implementation
    public void OnPlayBackStarted()
    {
        Dispatcher.UIThread.Post(() => UpdatePlayPauseButton());
    }

    public void OnPlayBackEnded()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _renderTimer?.Stop();
            UpdatePlayPauseButton();
        });
    }

    public void OnPlayBackStopped()
    {
        Dispatcher.UIThread.Post(() => UpdatePlayPauseButton());
    }

    public void OnPlayBackPaused()
    {
        Dispatcher.UIThread.Post(() => UpdatePlayPauseButton());
    }

    public void OnPlayBackResumed()
    {
        Dispatcher.UIThread.Post(() => UpdatePlayPauseButton());
    }

    public void OnPlayBackSeek(long timeMs, long offsetMs) { }
    public void OnPlayBackSeekChapter(int chapter) { }
    public void OnPlayBackSpeedChanged(float speed) { }
    public void OnPlayBackError(string error)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Show error message (could use a dialog or status bar)
            Console.WriteLine($"Playback error: {error}");
        });
    }
    public void OnAVChange() { }
    public void OnAVStarted() { }

    // INotifyPropertyChanged implementation
    public new event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public bool ShowControls
    {
        get => _showControls;
        set
        {
            if (_showControls != value)
            {
                _showControls = value;
                OnPropertyChanged();
            }
        }
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        set
        {
            if (Math.Abs(_progressPercent - value) > 0.01)
            {
                _progressPercent = value;
                OnPropertyChanged();
            }
        }
    }

    public string CurrentTimeFormatted
    {
        get => _currentTimeFormatted;
        set
        {
            if (_currentTimeFormatted != value)
            {
                _currentTimeFormatted = value;
                OnPropertyChanged();
            }
        }
    }

    public string TotalTimeFormatted
    {
        get => _totalTimeFormatted;
        set
        {
            if (_totalTimeFormatted != value)
            {
                _totalTimeFormatted = value;
                OnPropertyChanged();
            }
        }
    }
}
