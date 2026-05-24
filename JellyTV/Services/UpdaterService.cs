using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace JellyTV.Services;

/// <summary>
/// Checks GitHub Releases for a newer JellyTV build. Apply step is currently
/// a stub — it will land once the appliance install layout is settled so we
/// can do the swap + restart without bricking dev installs.
/// </summary>
public class UpdaterService
{
    // Override for testing against a fork / staging repo.
    private const string DefaultRepo = "crucifix86/JellyTV";

    private static readonly HttpClient Http = CreateClient();

    public Version CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public async Task<UpdateCheckResult> CheckForUpdateAsync()
    {
        var repo = Environment.GetEnvironmentVariable("JELLYTV_UPDATE_REPO") ?? DefaultRepo;
        var url = $"https://api.github.com/repos/{repo}/releases/latest";

        try
        {
            var release = await Http.GetFromJsonAsync<GitHubRelease>(url);
            if (release == null || string.IsNullOrEmpty(release.TagName))
            {
                return UpdateCheckResult.NoReleases();
            }

            if (!TryParseTag(release.TagName, out var latest))
            {
                return UpdateCheckResult.Error($"Could not parse release tag '{release.TagName}'");
            }

            var current = NormalizeForCompare(CurrentVersion);
            var hasUpdate = latest > current;

            return new UpdateCheckResult
            {
                CheckedAt = DateTime.UtcNow,
                CurrentVersion = current,
                LatestVersion = latest,
                HasUpdate = hasUpdate,
                ReleaseUrl = release.HtmlUrl,
                ReleaseNotes = release.Body,
                AssetUrl = FindTarballAsset(release),
            };
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // No releases yet on the repo — treat as "you're on the latest" rather than an error.
            return UpdateCheckResult.NoReleases();
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Error(ex.Message);
        }
    }

    public Task<bool> DownloadAndApplyAsync(UpdateCheckResult result, IProgress<double>? progress = null)
    {
        // Intentionally stubbed: real apply needs the appliance install layout
        // (probably /usr/local/share/jellytv + systemctl restart jellytv.service)
        // so we can do the swap atomically without trashing a dev clone.
        Console.WriteLine($"UpdaterService: would apply {result.LatestVersion} from {result.AssetUrl}");
        return Task.FromResult(false);
    }

    private static string? FindTarballAsset(GitHubRelease release)
    {
        if (release.Assets == null) return null;
        foreach (var asset in release.Assets)
        {
            if (asset.Name != null && (asset.Name.EndsWith(".tar.gz") || asset.Name.EndsWith(".deb")))
            {
                return asset.BrowserDownloadUrl;
            }
        }
        // Fall back to the source tarball GitHub auto-attaches to every release.
        return release.TarballUrl;
    }

    private static bool TryParseTag(string tag, out Version version)
    {
        var trimmed = tag.TrimStart('v', 'V');
        return Version.TryParse(trimmed, out version!);
    }

    // Version.CompareTo treats unset Build/Revision as -1, which makes 0.1.0 < 0.1.0.0.
    // Normalize both sides so "0.1.0" tags match the assembly's "0.1.0.0".
    private static Version NormalizeForCompare(Version v) => new(
        v.Major,
        v.Minor,
        Math.Max(v.Build, 0),
        Math.Max(v.Revision, 0));

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        // GitHub requires a UA on all API requests.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("JellyTV-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    private class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("tarball_url")] public string? TarballUrl { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; set; }
    }

    private class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}

public class UpdateCheckResult
{
    public DateTime CheckedAt { get; set; }
    public Version? CurrentVersion { get; set; }
    public Version? LatestVersion { get; set; }
    public bool HasUpdate { get; set; }
    public string? ReleaseUrl { get; set; }
    public string? ReleaseNotes { get; set; }
    public string? AssetUrl { get; set; }
    public string? ErrorMessage { get; set; }

    public bool IsError => ErrorMessage != null;

    public static UpdateCheckResult NoReleases() => new()
    {
        CheckedAt = DateTime.UtcNow,
        HasUpdate = false,
    };

    public static UpdateCheckResult Error(string message) => new()
    {
        CheckedAt = DateTime.UtcNow,
        ErrorMessage = message,
    };
}
