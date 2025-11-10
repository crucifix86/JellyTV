using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using JellyTV.Models;

namespace JellyTV.Services;

public class JellyfinClient
{
    private HttpClient _httpClient;
    private string? _serverUrl;
    private string? _accessToken;
    private string? _userId;

    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);
    public string? ServerUrl => _serverUrl;
    public string? UserId => _userId;

    public JellyfinClient()
    {
        _httpClient = new HttpClient();
    }

    public void SetServer(string serverUrl)
    {
        // Recreate HttpClient to avoid "already started" error
        _httpClient?.Dispose();
        _httpClient = new HttpClient();

        _serverUrl = serverUrl.TrimEnd('/');
        _httpClient.BaseAddress = new Uri(_serverUrl);

        var deviceId = "jellytv-" + Environment.MachineName.ToLowerInvariant().Replace(" ", "-");

        // Use the format WITH quotes like curl does - use TryAddWithoutValidation to avoid header value validation issues
        // Use Authorization header (not X-Emby-Authorization) for modern Jellyfin servers
        var authValue = $"MediaBrowser Client=\"JellyTV\", Device=\"Linux PC\", DeviceId=\"{deviceId}\", Version=\"1.0.0\"";
        Console.WriteLine($"Setting auth headers: {authValue}");

        try
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authValue);
            Console.WriteLine("Headers added successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error adding headers: {ex.Message}");
        }
    }

    public void SetAccessToken(string accessToken, string userId)
    {
        _accessToken = accessToken;
        _userId = userId;
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("MediaBrowser", $"Token=\"{_accessToken}\"");
        Console.WriteLine($"Access token set for user: {userId}");
    }

    public async Task<ServerInfo?> GetServerInfoAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<ServerInfo>("/System/Info/Public");
            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting server info: {ex.Message}");
            return null;
        }
    }

    public async Task<AuthenticationResult?> AuthenticateAsync(string username, string password)
    {
        try
        {
            Console.WriteLine($"Attempting auth for user: '{username}' (length: {username?.Length ?? 0})");
            Console.WriteLine($"Password length: {password?.Length ?? 0}");

            var authRequest = new
            {
                Username = username,
                Pw = password
            };

            var requestJson = JsonSerializer.Serialize(authRequest);
            Console.WriteLine($"Request JSON: {requestJson}");

            // Create manual request to ensure headers are sent correctly
            var request = new HttpRequestMessage(HttpMethod.Post, "/Users/AuthenticateByName");
            request.Content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");

            // Add authorization header manually to the request (not the client)
            var deviceId = "jellytv-" + Environment.MachineName.ToLowerInvariant().Replace(" ", "-");
            var authValue = $"MediaBrowser Client=\"JellyTV\", Device=\"Linux PC\", DeviceId=\"{deviceId}\", Version=\"1.0.0\"";
            request.Headers.TryAddWithoutValidation("Authorization", authValue);

            Console.WriteLine($"Authorization header on request: {authValue}");

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Authentication failed: {response.StatusCode}");
                Console.WriteLine($"Response: {errorContent}");
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<AuthenticationResult>();

            if (result?.AccessToken != null)
            {
                _accessToken = result.AccessToken;
                _userId = result.User?.Id;
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("MediaBrowser", $"Token=\"{_accessToken}\"");
            }

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Authentication error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return null;
        }
    }

    public async Task<BaseItemDto[]?> GetLatestMediaAsync(int limit = 20, string? parentId = null)
    {
        if (!IsAuthenticated) return null;

        try
        {
            var url = $"/Users/{_userId}/Items/Latest?Limit={limit}&Fields=BasicSyncInfo,Overview,ProductionYear";
            if (parentId != null)
            {
                url += $"&ParentId={parentId}";
            }

            // Latest endpoint returns an array directly, not a QueryResult
            var response = await _httpClient.GetFromJsonAsync<BaseItemDto[]>(url);
            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting latest media: {ex.Message}");
            return null;
        }
    }

    public async Task<QueryResult<BaseItemDto>?> GetResumeItemsAsync(int limit = 20)
    {
        if (!IsAuthenticated) return null;

        try
        {
            var response = await _httpClient.GetFromJsonAsync<QueryResult<BaseItemDto>>(
                $"/Users/{_userId}/Items/Resume?Limit={limit}&MediaTypes=Video&Fields=BasicSyncInfo,Overview");
            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting resume items: {ex.Message}");
            return null;
        }
    }

    public async Task<QueryResult<BaseItemDto>?> GetItemsAsync(
        string? parentId = null,
        string? includeItemTypes = null,
        int? startIndex = null,
        int? limit = null,
        string? sortBy = null)
    {
        if (!IsAuthenticated) return null;

        try
        {
            var queryParams = new System.Collections.Generic.List<string>
            {
                "Recursive=true",
                "Fields=BasicSyncInfo,Overview,ProductionYear"
            };

            if (parentId != null) queryParams.Add($"ParentId={parentId}");
            if (includeItemTypes != null) queryParams.Add($"IncludeItemTypes={includeItemTypes}");
            if (startIndex.HasValue) queryParams.Add($"StartIndex={startIndex}");
            if (limit.HasValue) queryParams.Add($"Limit={limit}");
            if (sortBy != null) queryParams.Add($"SortBy={sortBy}");

            var query = string.Join("&", queryParams);
            var response = await _httpClient.GetFromJsonAsync<QueryResult<BaseItemDto>>(
                $"/Users/{_userId}/Items?{query}");
            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting items: {ex.Message}");
            return null;
        }
    }

    public async Task<QueryResult<BaseItemDto>?> GetUserViewsAsync()
    {
        if (!IsAuthenticated) return null;

        try
        {
            var response = await _httpClient.GetFromJsonAsync<QueryResult<BaseItemDto>>(
                $"/Users/{_userId}/Views");
            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting user views: {ex.Message}");
            return null;
        }
    }

    public string? GetImageUrl(string itemId, string imageType = "Primary", int? width = null, int? height = null)
    {
        if (string.IsNullOrEmpty(_serverUrl)) return null;

        var url = $"{_serverUrl}/Items/{itemId}/Images/{imageType}";
        var queryParams = new System.Collections.Generic.List<string>();

        if (width.HasValue) queryParams.Add($"width={width}");
        if (height.HasValue) queryParams.Add($"height={height}");

        if (queryParams.Count > 0)
            url += "?" + string.Join("&", queryParams);

        return url;
    }

    public string? GetStreamUrl(string itemId)
    {
        if (string.IsNullOrEmpty(_serverUrl) || !IsAuthenticated || string.IsNullOrEmpty(_userId)) return null;

        // Use Jellyfin direct stream endpoint - simpler and more reliable
        return $"{_serverUrl}/Items/{itemId}/Download?api_key={_accessToken}";
    }

    public async Task<Bitmap?> LoadImageAsync(string itemId, string imageType = "Primary", int? width = null, int? height = null)
    {
        if (string.IsNullOrEmpty(_serverUrl)) return null;

        try
        {
            var url = $"/Items/{itemId}/Images/{imageType}";
            var queryParams = new System.Collections.Generic.List<string>();

            if (width.HasValue) queryParams.Add($"width={width}");
            if (height.HasValue) queryParams.Add($"height={height}");

            if (queryParams.Count > 0)
                url += "?" + string.Join("&", queryParams);

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to load image: {response.StatusCode}");
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            return new Bitmap(memoryStream);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading image: {ex.Message}");
            return null;
        }
    }

    public async Task<QueryResult<BaseItemDto>?> GetSeasonsAsync(string seriesId)
    {
        if (!IsAuthenticated) return null;

        try
        {
            var response = await _httpClient.GetFromJsonAsync<QueryResult<BaseItemDto>>(
                $"/Shows/{seriesId}/Seasons?UserId={_userId}&Fields=Overview");
            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting seasons: {ex.Message}");
            return null;
        }
    }

    public async Task<QueryResult<BaseItemDto>?> GetEpisodesAsync(string seriesId, string seasonId)
    {
        if (!IsAuthenticated) return null;

        try
        {
            var response = await _httpClient.GetFromJsonAsync<QueryResult<BaseItemDto>>(
                $"/Shows/{seriesId}/Episodes?SeasonId={seasonId}&UserId={_userId}&Fields=Overview");
            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting episodes: {ex.Message}");
            return null;
        }
    }

    public async Task<BaseItemDto?> GetItemDetailsAsync(string itemId)
    {
        if (!IsAuthenticated) return null;

        try
        {
            var response = await _httpClient.GetFromJsonAsync<BaseItemDto>(
                $"/Users/{_userId}/Items/{itemId}?Fields=People,Overview,ProviderIds");
            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting item details: {ex.Message}");
            return null;
        }
    }

    public async Task<BaseItemDto?> GetNextUpAsync(string seriesId)
    {
        if (!IsAuthenticated) return null;

        try
        {
            // Use the NextUp endpoint to get the next episode to watch for a series
            var response = await _httpClient.GetFromJsonAsync<QueryResult<BaseItemDto>>(
                $"/Shows/NextUp?SeriesId={seriesId}&UserId={_userId}&Fields=Overview");

            // Return the first next-up episode if available
            if (response?.Items != null && response.Items.Count > 0)
            {
                return response.Items[0];
            }

            // If no next-up episode, try to get the first episode of the first season
            Console.WriteLine("No next-up episode, getting first episode of series");
            var seasons = await GetSeasonsAsync(seriesId);
            if (seasons?.Items != null && seasons.Items.Count > 0)
            {
                var firstSeason = seasons.Items[0];
                if (firstSeason.Id != null)
                {
                    var episodes = await GetEpisodesAsync(seriesId, firstSeason.Id);
                    if (episodes?.Items != null && episodes.Items.Count > 0)
                    {
                        return episodes.Items[0];
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting next up episode: {ex.Message}");
            return null;
        }
    }
}
