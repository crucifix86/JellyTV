using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    private readonly MediaPlayerService _mediaPlayer;

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
    private bool _isPlaying;

    public MainWindowViewModel()
    {
        _jellyfinClient = new JellyfinClient();
        _mediaPlayer = new MediaPlayerService();

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
                ["UserId"] = userId
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

    [RelayCommand]
    private void PlayItem(BaseItemDto? item)
    {
        // Use the passed item if available, otherwise fall back to SelectedItem
        var itemToPlay = item ?? SelectedItem;

        if (itemToPlay?.Id == null)
        {
            Console.WriteLine("No item to play");
            return;
        }

        // Build the media URL from Jellyfin
        var mediaUrl = _jellyfinClient.GetStreamUrl(itemToPlay.Id);
        Console.WriteLine($"Playing: {itemToPlay.Name}");
        Console.WriteLine($"URL: {mediaUrl}");

        IsPlaying = true;
        _mediaPlayer.PlayMedia(mediaUrl);
    }
}
