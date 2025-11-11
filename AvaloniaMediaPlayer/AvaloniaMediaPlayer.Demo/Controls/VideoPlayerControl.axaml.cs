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

namespace AvaloniaMediaPlayer.Demo.Controls;

public partial class VideoPlayerControl : UserControl, INotifyPropertyChanged, IPlayerCallback
{
    private VideoPlayer? _player;
    private Image? _videoImage;
    private Button? _playPauseButton;
    private ProgressBar? _progressBar;
    private TextBlock? _subtitleText;
    private Grid? _controlsOverlay;
    private Border? _placeholderOverlay;
    private DispatcherTimer? _renderTimer;
    private DispatcherTimer? _hideControlsTimer;
    private WriteableBitmap? _videoBitmap;

    private bool _showControls = true;
    private double _progressPercent;
    private string _currentTimeFormatted = "00:00";
    private string _totalTimeFormatted = "00:00";

    public VideoPlayerControl()
    {
        InitializeComponent();
        DataContext = this;

        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16.67) // ~60 FPS for smoother rendering
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
            ShowControls = true;
            _hideControlsTimer.Stop();
            _hideControlsTimer.Start();
        };

        // Handle control unload to properly dispose player
        Unloaded += (s, e) =>
        {
            CleanupPlayer();
        };
    }

    private void CleanupPlayer()
    {
        _renderTimer?.Stop();
        _hideControlsTimer?.Stop();

        if (_player != null)
        {
            _player.Dispose();
            _player = null;
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
        _placeholderOverlay = this.FindControl<Border>("PlaceholderOverlay");

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

        // Settings button - show playback speed menu and media info
        var settingsButton = this.FindControl<Button>("SettingsButton");
        if (settingsButton != null)
        {
            settingsButton.Click += async (s, e) =>
            {
                if (_player == null) return;

                var speedOptions = new[] { "0.25x", "0.5x", "0.75x", "Normal", "1.25x", "1.5x", "2x" };
                var speedValues = new[] { 0.25f, 0.5f, 0.75f, 1.0f, 1.25f, 1.5f, 2.0f };

                var dialog = new Window
                {
                    Title = "Playback Settings & Media Info",
                    Width = 400,
                    Height = 450,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var stack = new StackPanel { Margin = new Thickness(20), Spacing = 15 };

                // Playback speed section
                stack.Children.Add(new TextBlock { Text = "Playback Speed:", FontWeight = FontWeight.Bold });
                var speedCombo = new ComboBox { ItemsSource = speedOptions, SelectedIndex = 3, Width = 150 };
                speedCombo.SelectionChanged += (_, __) =>
                {
                    if (speedCombo.SelectedIndex >= 0)
                    {
                        _player.SetSpeed(speedValues[speedCombo.SelectedIndex]);
                    }
                };
                stack.Children.Add(speedCombo);

                // Video info section
                if (_player.HasVideo)
                {
                    stack.Children.Add(new TextBlock { Text = "Video Stream:", FontWeight = FontWeight.Bold, Margin = new Thickness(0, 10, 0, 0) });
                    _player.GetVideoStreamInfo(0, out var videoInfo);

                    stack.Children.Add(new TextBlock { Text = $"Codec: {videoInfo.Codec}" });
                    stack.Children.Add(new TextBlock { Text = $"Resolution: {videoInfo.Width}x{videoInfo.Height}" });
                    stack.Children.Add(new TextBlock { Text = $"Frame Rate: {videoInfo.FrameRate:F2} fps" });
                    if (videoInfo.Bitrate > 0)
                        stack.Children.Add(new TextBlock { Text = $"Bitrate: {videoInfo.Bitrate / 1000} kbps" });
                }

                // Audio info section
                if (_player.HasAudio)
                {
                    stack.Children.Add(new TextBlock { Text = "Audio Stream:", FontWeight = FontWeight.Bold, Margin = new Thickness(0, 10, 0, 0) });
                    _player.GetAudioStreamInfo(0, out var audioInfo);

                    stack.Children.Add(new TextBlock { Text = $"Codec: {audioInfo.Codec}" });
                    stack.Children.Add(new TextBlock { Text = $"Sample Rate: {audioInfo.SampleRate} Hz" });
                    stack.Children.Add(new TextBlock { Text = $"Channels: {audioInfo.Channels}" });
                    if (audioInfo.Bitrate > 0)
                        stack.Children.Add(new TextBlock { Text = $"Bitrate: {audioInfo.Bitrate / 1000} kbps" });
                }

                dialog.Content = stack;
                var window = TopLevel.GetTopLevel(this) as Window;
                if (window != null)
                {
                    await dialog.ShowDialog(window);
                }
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
            // Hide placeholder when video starts
            if (_placeholderOverlay != null)
                _placeholderOverlay.IsVisible = false;

            _renderTimer?.Start();
            _hideControlsTimer?.Start();
        }
    }

    private void RenderTimer_Tick(object? sender, EventArgs e)
    {
        if (_player == null || !_player.IsPlaying)
            return;

        // Get current video frame
        var frame = _player.GetCurrentVideoFrame();
        if (frame != null)
        {
            RenderVideoFrame(frame);
        }

        // Update time display
        var currentMs = _player.GetTime();
        var totalMs = _player.GetTotalTime();

        CurrentTimeFormatted = FormatTime(currentMs);
        TotalTimeFormatted = FormatTime(totalMs);
        ProgressPercent = totalMs > 0 ? (currentMs * 100.0) / totalMs : 0;

        // Update subtitle display
        var overlays = _player.GetOverlayContainer().GetOverlays(currentMs / 1000.0);
        var textOverlay = overlays.FirstOrDefault(o => o is Core.Overlay.DVDOverlayText) as Core.Overlay.DVDOverlayText;
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

            // Show placeholder when playback ends
            if (_placeholderOverlay != null)
                _placeholderOverlay.IsVisible = true;
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
