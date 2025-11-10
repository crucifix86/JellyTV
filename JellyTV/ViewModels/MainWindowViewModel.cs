using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JellyTV.Models;
using JellyTV.Services;

namespace JellyTV.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly JellyfinClient _jellyfinClient;
    private CancellationTokenSource? _libraryLoadCancellation;

    // Actions to control the VideoPlayerControl (set by MainWindow)
    public Action<string>? PlayVideoAction { get; set; }
    public Action? TogglePlayPauseAction { get; set; }
    public Action? StopPlaybackAction { get; set; }
    public Action<int>? SeekAction { get; set; }
    public Func<long>? GetPositionFunc { get; set; }
    public Func<long>? GetDurationFunc { get; set; }

    [ObservableProperty]
    private string _serverAddress = "";

    [ObservableProperty]
    private string _serverPort = "8096";

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private ObservableCollection<BaseItemDto> _resumeItems = new();

    [ObservableProperty]
    private ObservableCollection<BaseItemDto> _latestItems = new();

    [ObservableProperty]
    private ObservableCollection<BaseItemDto> _libraries = new();

    [ObservableProperty]
    private ObservableCollection<MediaRow> _libraryRows = new();

    [ObservableProperty]
    private BaseItemDto? _selectedItem;

    [ObservableProperty]
    private bool _showDetailView;

    [ObservableProperty]
    private bool _showCastCrewView;

    [ObservableProperty]
    private ObservableCollection<BaseItemDto> _seasons = new();

    [ObservableProperty]
    private ObservableCollection<BaseItemDto> _episodes = new();

    [ObservableProperty]
    private string _librarySectionHeading = "Seasons";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowVideo))]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowVideo))]
    private bool _showPlayerControls;

    // Computed property to show video only when playing and controls are hidden
    public bool ShowVideo => IsPlaying && !ShowPlayerControls;

    [ObservableProperty]
    private long _playbackPosition;

    [ObservableProperty]
    private long _playbackDuration;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private string _hardwareAcceleration = "Auto";

    // VideoPlayerControl handles all media info and rendering

    private System.Threading.Timer? _controlsHideTimer;
    private System.Threading.Timer? _playbackUpdateTimer;

    public List<string> HardwareAccelerationOptions { get; } = new List<string>
    {
        "Auto (Let VLC choose)",
        "NVIDIA (NVDEC)",
        "AMD/Intel (VA-API)",
        "NVIDIA Legacy (VDPAU)",
        "Disabled (Software only)"
    };

    public MainWindowViewModel()
    {
        _jellyfinClient = new JellyfinClient();

        // Load saved credentials
        LoadCredentials();
    }

    private void LoadCredentials()
    {
        try
        {
            var configPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "JellyTV",
                "config.json"
            );

            if (System.IO.File.Exists(configPath))
            {
                var json = System.IO.File.ReadAllText(configPath);
                var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (config != null)
                {
                    if (config.ContainsKey("ServerAddress")) ServerAddress = config["ServerAddress"];
                    if (config.ContainsKey("ServerPort")) ServerPort = config["ServerPort"];
                    if (config.ContainsKey("Username")) Username = config["Username"];
                    if (config.ContainsKey("HardwareAcceleration")) HardwareAcceleration = config["HardwareAcceleration"];
                    if (config.ContainsKey("AccessToken") && config.ContainsKey("UserId"))
                    {
                        // Auto-login with saved token
                        _jellyfinClient.SetServer($"http://{ServerAddress}:{ServerPort}");
                        _jellyfinClient.SetAccessToken(config["AccessToken"], config["UserId"]);
                        IsAuthenticated = true;
                        Task.Run(async () => await LoadHomeDataAsync());
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading credentials: {ex.Message}");
        }
    }

    private void SaveCredentials(string accessToken, string userId)
    {
        try
        {
            var configDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "JellyTV"
            );

            System.IO.Directory.CreateDirectory(configDir);

            var config = new Dictionary<string, string>
            {
                ["ServerAddress"] = ServerAddress,
                ["ServerPort"] = ServerPort,
                ["Username"] = Username,
                ["AccessToken"] = accessToken,
                ["UserId"] = userId,
                ["HardwareAcceleration"] = HardwareAcceleration
            };

            var json = System.Text.Json.JsonSerializer.Serialize(config);
            var configPath = System.IO.Path.Combine(configDir, "config.json");
            System.IO.File.WriteAllText(configPath, json);

            Console.WriteLine($"Credentials saved to {configPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving credentials: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SelectItemAsync(BaseItemDto item)
    {
        if (item == null) return;

        Console.WriteLine($"Item selected: {item.Name} (Type: {item.Type})");

        // Handle Season type specially - it needs the parent series context
        if (item.Type == "Season")
        {
            await SelectSeasonAsync(item);
            return;
        }

        // Load detailed info with cast
        if (item.Id != null)
        {
            var detailedItem = await _jellyfinClient.GetItemDetailsAsync(item.Id);
            if (detailedItem != null)
            {
                // Preserve the image bitmap from the original item, or load it if missing
                if (item.ImageBitmap != null)
                {
                    detailedItem.ImageBitmap = item.ImageBitmap;
                }
                else
                {
                    detailedItem.ImageBitmap = await _jellyfinClient.LoadImageAsync(item.Id, "Primary", 400, 600);
                }

                SelectedItem = detailedItem;
                Console.WriteLine($"Cast info loaded: {SelectedItem.CastList ?? "No cast"}");
            }
            else
            {
                SelectedItem = item;
            }
        }
        else
        {
            SelectedItem = item;
        }

        ShowDetailView = true;

        // If it's a TV show (Series), load its seasons
        if (SelectedItem.Type == "Series" && SelectedItem.Id != null)
        {
            await LoadSeasonsAsync(SelectedItem.Id);
        }
    }

    [RelayCommand]
    private void GoBackToHome()
    {
        ShowDetailView = false;
        ShowCastCrewView = false;
        SelectedItem = null;
        Seasons.Clear();
        Episodes.Clear();
    }

    [RelayCommand]
    private async Task LoadLibrary(BaseItemDto? library)
    {
        if (library?.Id == null) return;

        // Cancel any previous library load operation
        _libraryLoadCancellation?.Cancel();
        _libraryLoadCancellation?.Dispose();
        _libraryLoadCancellation = new CancellationTokenSource();
        var cancellationToken = _libraryLoadCancellation.Token;

        Console.WriteLine($"Loading library: {library.Name}, CollectionType: {library.CollectionType}");

        // Clear existing data
        ShowDetailView = false;
        ShowCastCrewView = false;
        SelectedItem = null;
        Seasons.Clear();
        Episodes.Clear();

        // Determine item type based on CollectionType (matching Jellyfin pattern)
        string? includeItemTypes = null;

        if (library.CollectionType == "movies")
        {
            includeItemTypes = "Movie";
            LibrarySectionHeading = "Movies";
        }
        else if (library.CollectionType == "tvshows")
        {
            includeItemTypes = "Series";
            LibrarySectionHeading = "Series";
        }
        else
        {
            // For other library types, show all items
            LibrarySectionHeading = library.Name ?? "Items";
        }

        Console.WriteLine($"Fetching items with IncludeItemTypes={includeItemTypes}, Recursive=true");

        // Call API with same parameters as Jellyfin (Recursive=true, sorted by SortName)
        var result = await _jellyfinClient.GetItemsAsync(
            parentId: library.Id,
            includeItemTypes: includeItemTypes,
            sortBy: "SortName",
            recursive: true);

        if (result?.Items != null && !cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine($"Loaded {result.Items.Count} items from {library.Name}");

            foreach (var item in result.Items)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine("Library load cancelled");
                    return;
                }

                // Load image for each item
                if (item.Id != null)
                {
                    item.ImageBitmap = await _jellyfinClient.LoadImageAsync(item.Id, "Primary", 300, 450);
                }

                Seasons.Add(item);
            }

            Console.WriteLine($"Finished loading {Seasons.Count} items");

            // Set up view
            SelectedItem = library;
            ShowDetailView = true;
        }
    }

    [RelayCommand]
    private async Task ShowCastCrewAsync()
    {
        if (SelectedItem?.People != null && SelectedItem.People.Count > 0)
        {
            // Load images for cast members who don't have them yet
            foreach (var person in SelectedItem.People)
            {
                if (person.ImageBitmap == null && person.Id != null)
                {
                    person.ImageBitmap = await _jellyfinClient.LoadImageAsync(person.Id, "Primary", 300, 300);
                    Console.WriteLine($"Loaded image for {person.Name}: {person.ImageBitmap != null}");
                }
            }
            ShowCastCrewView = true;
        }
    }

    [RelayCommand]
    private void CloseCastCrew()
    {
        ShowCastCrewView = false;
    }

    private async Task LoadSeasonsAsync(string seriesId)
    {
        Console.WriteLine($"Loading seasons for series ID: {seriesId}");
        Seasons.Clear();

        // Get seasons for the series
        var seasonsResult = await _jellyfinClient.GetSeasonsAsync(seriesId);
        if (seasonsResult?.Items != null)
        {
            foreach (var season in seasonsResult.Items)
            {
                if (season.Id != null)
                {
                    season.ImageBitmap = await _jellyfinClient.LoadImageAsync(season.Id, "Primary", 400, 600);
                }
                Seasons.Add(season);
            }
            Console.WriteLine($"Loaded {Seasons.Count} seasons");
        }
    }

    [RelayCommand]
    private async Task SelectSeasonAsync(BaseItemDto season)
    {
        if (season?.Id == null || SelectedItem?.Id == null) return;

        Console.WriteLine($"Season selected: {season.Name}");
        Episodes.Clear();

        // Get episodes for the season (need both series ID and season ID)
        var episodesResult = await _jellyfinClient.GetEpisodesAsync(SelectedItem.Id, season.Id);
        if (episodesResult?.Items != null)
        {
            foreach (var episode in episodesResult.Items)
            {
                if (episode.Id != null)
                {
                    episode.ImageBitmap = await _jellyfinClient.LoadImageAsync(episode.Id, "Primary", 600, 338);
                }
                Episodes.Add(episode);
            }
            Console.WriteLine($"Loaded {Episodes.Count} episodes");
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        StatusMessage = null;

        if (string.IsNullOrWhiteSpace(ServerAddress) || string.IsNullOrWhiteSpace(Username))
        {
            StatusMessage = "Please enter server address and username";
            return;
        }

        try
        {
            var serverUrl = $"http://{ServerAddress}:{ServerPort}";
            Console.WriteLine($"Connecting to {serverUrl} as {Username}");
            _jellyfinClient.SetServer(serverUrl);

            Console.WriteLine("Getting server info...");
            var serverInfo = await _jellyfinClient.GetServerInfoAsync();
            if (serverInfo == null)
            {
                StatusMessage = "Could not connect to server. Check the address and port.";
                Console.WriteLine($"ERROR: Could not connect to server {serverUrl}");
                return;
            }

            Console.WriteLine($"Server found: {serverInfo.ServerName}");
            var authResult = await _jellyfinClient.AuthenticateAsync(Username, Password);
            if (authResult?.AccessToken == null || authResult.User?.Id == null)
            {
                StatusMessage = "Authentication failed. Check your credentials.";
                Console.WriteLine($"ERROR: Authentication failed for user {Username}");
                return;
            }

            Console.WriteLine("Authentication successful!");

            // Save credentials for auto-login
            SaveCredentials(authResult.AccessToken, authResult.User.Id);

            IsAuthenticated = true;
            await LoadHomeDataAsync();
            Console.WriteLine("Home data loaded!");
        }
        catch (System.Exception ex)
        {
            Console.WriteLine($"EXCEPTION in ConnectAsync: {ex}");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private async Task LoadHomeDataAsync()
    {
        // Load libraries/views
        var viewsResult = await _jellyfinClient.GetUserViewsAsync();
        if (viewsResult?.Items != null)
        {
            Libraries.Clear();
            foreach (var item in viewsResult.Items)
            {
                // Load 4 random preview images for Emby-style library tiles
                var latestForPreview = await _jellyfinClient.GetLatestMediaAsync(4, item.Id);
                if (latestForPreview != null && latestForPreview.Length > 0)
                {
                    item.LibraryPreviewImages = new List<Avalonia.Media.Imaging.Bitmap>();
                    foreach (var previewItem in latestForPreview)
                    {
                        if (previewItem.Id != null)
                        {
                            var img = await _jellyfinClient.LoadImageAsync(previewItem.Id, "Primary", 200, 300);
                            if (img != null)
                            {
                                item.LibraryPreviewImages.Add(img);
                            }
                        }
                    }
                    Console.WriteLine($"Loaded {item.LibraryPreviewImages.Count} preview images for {item.Name}");
                }
                Libraries.Add(item);
            }
            Console.WriteLine($"Loaded {Libraries.Count} libraries");
        }

        // Load resume items (Continue Watching)
        var resumeResult = await _jellyfinClient.GetResumeItemsAsync(10);
        if (resumeResult?.Items != null)
        {
            ResumeItems.Clear();
            foreach (var item in resumeResult.Items)
            {
                // Load image bitmap for backdrop (landscape) format for resume items
                if (item.Id != null)
                {
                    item.ImageBitmap = await _jellyfinClient.LoadImageAsync(item.Id, "Primary", 600, 338);
                    Console.WriteLine($"Resume item: {item.Name}, Image loaded: {item.ImageBitmap != null}");
                }
                ResumeItems.Add(item);
            }
            Console.WriteLine($"Loaded {ResumeItems.Count} resume items");
        }

        // Load latest items per library (like Android TV app does)
        LibraryRows.Clear();
        foreach (var library in Libraries)
        {
            var latestItems = await _jellyfinClient.GetLatestMediaAsync(20, library.Id);
            if (latestItems != null && latestItems.Length > 0)
            {
                var row = new MediaRow
                {
                    Title = $"Latest in {library.Name}"
                };

                foreach (var item in latestItems)
                {
                    // Load image bitmap for poster (portrait) format for latest items
                    if (item.Id != null)
                    {
                        item.ImageBitmap = await _jellyfinClient.LoadImageAsync(item.Id, "Primary", 400, 600);
                        Console.WriteLine($"Latest in {library.Name}: {item.Name}, Image loaded: {item.ImageBitmap != null}");
                    }
                    row.Items.Add(item);
                }

                LibraryRows.Add(row);
                Console.WriteLine($"Created row '{row.Title}' with {row.Items.Count} items");
            }
        }
    }

    partial void OnHardwareAccelerationChanged(string value)
    {
        // Save the hardware acceleration setting when it changes
        try
        {
            var configPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "JellyTV",
                "config.json"
            );

            if (System.IO.File.Exists(configPath))
            {
                var json = System.IO.File.ReadAllText(configPath);
                var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (config != null)
                {
                    config["HardwareAcceleration"] = value;
                    var updatedJson = System.Text.Json.JsonSerializer.Serialize(config);
                    System.IO.File.WriteAllText(configPath, updatedJson);
                    Console.WriteLine($"Hardware acceleration setting updated to: {value}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving hardware acceleration setting: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task PlayItem(BaseItemDto? item)
    {
        // Use the passed item if available, otherwise fall back to SelectedItem
        var itemToPlay = item ?? SelectedItem;

        if (itemToPlay?.Id == null)
        {
            Console.WriteLine("No item to play");
            return;
        }

        // If it's a Series, get the next episode to play (resume or first unwatched)
        if (itemToPlay.Type == "Series")
        {
            Console.WriteLine($"Series selected: {itemToPlay.Name}, getting next episode to play");
            var nextEpisode = await _jellyfinClient.GetNextUpAsync(itemToPlay.Id);

            if (nextEpisode != null)
            {
                itemToPlay = nextEpisode;
                Console.WriteLine($"Found next episode: {nextEpisode.Name}");
            }
            else
            {
                Console.WriteLine("No next episode found for series");
                return;
            }
        }

        // Build the media URL from Jellyfin
        var mediaUrl = _jellyfinClient.GetStreamUrl(itemToPlay.Id);
        Console.WriteLine($"Playing: {itemToPlay.Name}");
        Console.WriteLine($"URL: {mediaUrl}");

        IsPlaying = true;
        IsPaused = false;

        // Use VideoPlayerControl for playback
        PlayVideoAction?.Invoke(mediaUrl);
    }

    // Player controls handled by VideoPlayerControl
    [RelayCommand]
    private void TogglePlayPause()
    {
        TogglePlayPauseAction?.Invoke();
        IsPaused = !IsPaused;
    }

    [RelayCommand]
    private void SeekForward()
    {
        SeekAction?.Invoke(10);
    }

    [RelayCommand]
    private void SeekBackward()
    {
        SeekAction?.Invoke(-10);
    }

    [RelayCommand]
    private void ShowControls()
    {
        Console.WriteLine($"ShowControls called: IsPlaying={IsPlaying}");
        if (IsPlaying)
        {
            ShowPlayerControls = true;
            Console.WriteLine($"Player controls shown");
            ResetControlsHideTimer();
        }
        else
        {
            Console.WriteLine("Not playing - controls won't show");
        }
    }

    [RelayCommand]
    private void HideControls()
    {
        ShowPlayerControls = false;
    }

    [RelayCommand]
    private void StopPlayback()
    {
        Console.WriteLine("StopPlayback called - setting IsPlaying to false");
        // Call VideoPlayerControl to stop
        StopPlaybackAction?.Invoke();
        IsPlaying = false;
        IsPaused = false;
        ShowPlayerControls = false;
        StopPlaybackUpdateTimer();
        StopControlsHideTimer();
        Console.WriteLine($"StopPlayback complete - IsPlaying={IsPlaying}");
    }

    private void StartPlaybackUpdateTimer()
    {
        StopPlaybackUpdateTimer();

        // VideoPlayerControl manages its own playback timing
        // No timer needed
    }

    private void StopPlaybackUpdateTimer()
    {
        _playbackUpdateTimer?.Dispose();
        _playbackUpdateTimer = null;
    }

    private void ResetControlsHideTimer()
    {
        StopControlsHideTimer();

        // Hide controls after 5 seconds of inactivity
        _controlsHideTimer = new System.Threading.Timer(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ShowPlayerControls = false;
            });
        }, null, 5000, System.Threading.Timeout.Infinite);
    }

    private void StopControlsHideTimer()
    {
        _controlsHideTimer?.Dispose();
        _controlsHideTimer = null;
    }

    public string FormattedPosition => FormatTime(PlaybackPosition);
    public string FormattedDuration => FormatTime(PlaybackDuration);

    private string FormatTime(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(milliseconds);
        if (time.TotalHours >= 1)
        {
            return time.ToString(@"h\:mm\:ss");
        }
        return time.ToString(@"m\:ss");
    }
}
