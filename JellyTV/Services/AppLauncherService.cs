using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace JellyTV.Services;

/// <summary>
/// Launches external sub-apps (currently just the YouTube Electron app) and
/// awaits their exit so the caller can refocus the main window.
/// </summary>
public class AppLauncherService
{
    public event Action? AppExited;

    public bool IsAppRunning => _currentProcess is { HasExited: false };

    private Process? _currentProcess;

    public Task<bool> LaunchYouTubeAsync()
    {
        if (IsAppRunning)
        {
            Console.WriteLine("AppLauncher: an app is already running, ignoring launch request");
            return Task.FromResult(false);
        }

        var appDir = ResolveYouTubeAppDir();
        if (appDir == null)
        {
            Console.WriteLine("AppLauncher: could not locate apps/youtube — is it built?");
            return Task.FromResult(false);
        }

        var psi = new ProcessStartInfo
        {
            FileName = "npm",
            Arguments = IsWayland() ? "run start:wayland" : "start",
            WorkingDirectory = appDir,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };

        Console.WriteLine($"AppLauncher: launching YouTube from {appDir} ({psi.Arguments})");

        try
        {
            _currentProcess = Process.Start(psi);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AppLauncher: failed to start YouTube app: {ex.Message}");
            return Task.FromResult(false);
        }

        if (_currentProcess == null)
        {
            return Task.FromResult(false);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _currentProcess.WaitForExitAsync();
            }
            finally
            {
                Console.WriteLine($"AppLauncher: YouTube app exited (code {_currentProcess?.ExitCode})");
                _currentProcess = null;
                AppExited?.Invoke();
            }
        });

        return Task.FromResult(true);
    }

    private static bool IsWayland()
    {
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
            || string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveYouTubeAppDir()
    {
        // Override for packaged installs (e.g. the kernel image).
        var envOverride = Environment.GetEnvironmentVariable("JELLYTV_YOUTUBE_DIR");
        if (!string.IsNullOrEmpty(envOverride) && File.Exists(Path.Combine(envOverride, "main.js")))
        {
            return envOverride;
        }

        // Walk up from the running binary looking for apps/youtube/main.js.
        // Covers both dev (bin/Debug/net8.0) and publish layouts.
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, "apps", "youtube", "main.js");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate);
            }
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}
