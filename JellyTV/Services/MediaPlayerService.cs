using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace JellyTV.Services;

public class MediaPlayerService
{
    private Process? _playerProcess;
    private readonly string _mpvPath;
    private IntPtr _windowHandle;

    public MediaPlayerService()
    {
        // Use bundled MPV AppImage from bin folder
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var assemblyDir = Path.GetDirectoryName(assemblyLocation);
        var mpvBinPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(assemblyDir ?? "")))) ?? "", "bin", "mpv.AppImage");

        _mpvPath = File.Exists(mpvBinPath) ? mpvBinPath : "mpv";
        Console.WriteLine($"MPV path: {_mpvPath}");
    }

    public void SetWindowHandle(IntPtr handle)
    {
        _windowHandle = handle;
        Console.WriteLine($"MPV window handle set to: {handle}");
    }

    public void PlayMedia(string url)
    {
        try
        {
            // Stop any currently playing media
            StopPlayback();

            Console.WriteLine($"Starting MPV playback: {url}");
            Console.WriteLine($"Using window handle: {_windowHandle}");

            // Start MPV embedded in the native control using --wid
            var arguments = _windowHandle != IntPtr.Zero
                ? $"--wid={_windowHandle} --hwdec=auto \"{url}\""
                : $"--fs --hwdec=auto \"{url}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = _mpvPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = false
            };

            _playerProcess = Process.Start(startInfo);

            if (_playerProcess != null)
            {
                Console.WriteLine($"MPV started with PID: {_playerProcess.Id}");
            }
            else
            {
                Console.WriteLine("Failed to start MPV process");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting MPV: {ex.Message}");
        }
    }

    public void StopPlayback()
    {
        Console.WriteLine("StopPlayback called");

        if (_playerProcess != null && !_playerProcess.HasExited)
        {
            try
            {
                Console.WriteLine($"Killing MPV process with PID: {_playerProcess.Id}");
                _playerProcess.Kill(true); // Kill entire process tree
                _playerProcess.WaitForExit(2000);
                Console.WriteLine("MPV process killed successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping MPV via process: {ex.Message}");
            }
        }

        // Also use killall as fallback to ensure MPV is really dead
        try
        {
            var killProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "killall",
                Arguments = "-9 mpv",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            killProcess?.WaitForExit(1000);
            Console.WriteLine("killall mpv executed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error running killall: {ex.Message}");
        }

        _playerProcess = null;
    }

    public bool IsPlaying => _playerProcess != null && !_playerProcess.HasExited;
}
