using Avalonia.Controls;
using Avalonia.Platform.Storage;
using AvaloniaMediaPlayer.Demo.Controls;
using System;
using System.Linq;

namespace AvaloniaMediaPlayer.Demo;

public partial class MainWindow : Window
{
    private VideoPlayerControl? _playerControl;
    private Button? _openFileButton;
    private TextBlock? _fileNameText;

    public MainWindow()
    {
        InitializeComponent();

        _playerControl = this.FindControl<VideoPlayerControl>("PlayerControl");
        _openFileButton = this.FindControl<Button>("OpenFileButton");
        _fileNameText = this.FindControl<TextBlock>("FileNameText");

        if (_openFileButton != null)
        {
            _openFileButton.Click += async (s, e) =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Video File",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Video Files")
                        {
                            Patterns = new[] { "*.mp4", "*.mkv", "*.avi", "*.mov", "*.wmv", "*.flv", "*.webm", "*.m4v" }
                        },
                        new FilePickerFileType("All Files")
                        {
                            Patterns = new[] { "*.*" }
                        }
                    }
                });

                if (files.Count > 0)
                {
                    var file = files[0];
                    var path = file.Path.LocalPath;

                    if (_fileNameText != null)
                    {
                        _fileNameText.Text = file.Name;
                    }

                    _playerControl?.OpenFile(path);
                }
            };
        }

        // Cleanup when window closes
        Closing += (s, e) =>
        {
            Console.WriteLine("Window closing - cleaning up player");
        };
    }
}