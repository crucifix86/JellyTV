using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace JellyTV.Services;

public class InstallTarget
{
    public string Device { get; set; } = "";       // e.g. /dev/nvme0n1
    public string SizeHuman { get; set; } = "";    // e.g. 476.9G
    public string Model { get; set; } = "";
    public string Transport { get; set; } = "";    // nvme, sata, usb
    public bool IsLiveSource { get; set; }         // the booted-from USB
}

/// <summary>
/// Drives /usr/local/bin/jellytv-install to copy the live system onto a
/// permanent disk. Lists candidate disks (excluding the booted-from USB),
/// streams the script's progress lines so the UI can render them, and
/// signals when the install is complete.
/// </summary>
public class InstallerService
{
    private const string InstallScript = "/usr/local/bin/jellytv-install";

    public event Action<string>? ProgressLine;
    public event Action<bool>? Finished; // ok / failed

    /// <summary>
    /// Whole disks (TYPE=disk), excluding the live source. Filters out
    /// loop/cd-rom devices automatically — lsblk's TYPE column does that.
    /// </summary>
    public async Task<List<InstallTarget>> ListTargetsAsync()
    {
        var results = new List<InstallTarget>();
        var srcDevice = await GetLiveSourceDeviceAsync();

        try
        {
            // -J = JSON, -d = no partitions, -b = bytes for parsing, -o = fields
            var psi = new ProcessStartInfo
            {
                FileName = "lsblk",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in new[] { "-J", "-d", "-b", "-o", "NAME,SIZE,MODEL,TRAN,TYPE" })
                psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi)!;
            var json = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            using var doc = JsonDocument.Parse(json);
            foreach (var dev in doc.RootElement.GetProperty("blockdevices").EnumerateArray())
            {
                var name = "/dev/" + dev.GetProperty("name").GetString();
                var type = dev.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                if (type != "disk") continue;

                long sizeBytes = dev.GetProperty("size").GetInt64();
                results.Add(new InstallTarget
                {
                    Device = name,
                    SizeHuman = FormatSize(sizeBytes),
                    Model = dev.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "",
                    Transport = dev.TryGetProperty("tran", out var tr) ? tr.GetString() ?? "" : "",
                    IsLiveSource = string.Equals(name, srcDevice, StringComparison.OrdinalIgnoreCase),
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"InstallerService.ListTargetsAsync: {ex.Message}");
        }

        return results;
    }

    public async Task<bool> InstallAsync(string targetDevice)
    {
        if (!File.Exists(InstallScript))
        {
            ProgressLine?.Invoke($"ERR: install script {InstallScript} not present");
            Finished?.Invoke(false);
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "sudo",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // -n: never prompt for a password. jellytv has NOPASSWD on the
        // appliance; if for some reason it doesn't, fail loudly rather
        // than hang waiting for tty input that will never come.
        foreach (var a in new[] { "-n", InstallScript, targetDevice })
            psi.ArgumentList.Add(a);

        Process proc;
        try
        {
            proc = Process.Start(psi)!;
        }
        catch (Exception ex)
        {
            ProgressLine?.Invoke($"ERR: failed to start installer: {ex.Message}");
            Finished?.Invoke(false);
            return false;
        }

        // Stream both stdout and stderr line-by-line back to the UI.
        var stdoutTask = StreamLinesAsync(proc.StandardOutput);
        var stderrTask = StreamLinesAsync(proc.StandardError);

        await Task.WhenAll(stdoutTask, stderrTask);
        await proc.WaitForExitAsync();

        var ok = proc.ExitCode == 0;
        Finished?.Invoke(ok);
        return ok;
    }

    public static async Task RebootAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sudo",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in new[] { "-n", "systemctl", "reboot" })
                psi.ArgumentList.Add(a);
            Process.Start(psi);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"InstallerService.RebootAsync: {ex.Message}");
        }
    }

    private async Task StreamLinesAsync(StreamReader reader)
    {
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            ProgressLine?.Invoke(line);
        }
    }

    /// <summary>
    /// Walks the live-boot mount to find the base device of the boot media,
    /// so we can mark it in the disk list and the script can refuse to wipe it.
    /// </summary>
    private static async Task<string?> GetLiveSourceDeviceAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "findmnt",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in new[] { "-no", "SOURCE", "/run/live/medium" })
                psi.ArgumentList.Add(a);
            using var proc = Process.Start(psi)!;
            var src = (await proc.StandardOutput.ReadToEndAsync()).Trim();
            await proc.WaitForExitAsync();
            if (string.IsNullOrEmpty(src)) return null;

            // Strip partition suffix to get the base disk.
            var psi2 = new ProcessStartInfo
            {
                FileName = "lsblk",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in new[] { "-no", "PKNAME", src })
                psi2.ArgumentList.Add(a);
            using var proc2 = Process.Start(psi2)!;
            var pk = (await proc2.StandardOutput.ReadToEndAsync()).Trim();
            await proc2.WaitForExitAsync();
            return string.IsNullOrEmpty(pk) ? src : "/dev/" + pk;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatSize(long bytes)
    {
        const double GB = 1024.0 * 1024 * 1024;
        const double TB = GB * 1024;
        if (bytes >= TB) return $"{bytes / TB:F1} TB";
        if (bytes >= GB) return $"{bytes / GB:F1} GB";
        return $"{bytes / (1024 * 1024):F0} MB";
    }
}
