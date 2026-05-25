using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JellyTV.Services;

public class WiFiNetwork
{
    public string Ssid { get; set; } = "";
    public int SignalStrength { get; set; }       // 0–100
    public string Security { get; set; } = "";    // e.g. "WPA2", "WPA1 WPA2", "" for open
    public bool InUse { get; set; }
    public bool IsSecured => !string.IsNullOrEmpty(Security) && Security != "--";
}

/// <summary>
/// Wraps nmcli (NetworkManager) for WiFi scan/connect/disconnect. NetworkManager
/// must be installed on the host — provisioned via the image package list on
/// the appliance, and developers running JellyTV on a desktop already have it.
/// </summary>
public class WiFiService
{
    public async Task<List<WiFiNetwork>> ScanAsync()
    {
        var results = new List<WiFiNetwork>();
        try
        {
            // -t terse, -f field-list — stable parser-friendly output.
            // --rescan yes forces nmcli to trigger a fresh scan before listing.
            var output = await RunNmcliAsync(new[]
            {
                "-t", "-f", "IN-USE,SSID,SIGNAL,SECURITY",
                "device", "wifi", "list", "--rescan", "yes",
            }, timeoutMs: 15000);

            var seen = new HashSet<string>();
            foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                // Format: IN-USE:SSID:SIGNAL:SECURITY    (colons in SSID are escaped as "\:")
                var fields = SplitNmcliLine(rawLine);
                if (fields.Length < 4) continue;

                var ssid = fields[1];
                if (string.IsNullOrWhiteSpace(ssid)) continue;
                if (!seen.Add(ssid)) continue;   // dedupe access-point duplicates

                int.TryParse(fields[2], out var signal);

                results.Add(new WiFiNetwork
                {
                    InUse = fields[0] == "*",
                    Ssid = ssid,
                    SignalStrength = signal,
                    Security = fields[3],
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WiFiService.ScanAsync error: {ex.Message}");
        }

        return results.OrderByDescending(n => n.InUse)
                      .ThenByDescending(n => n.SignalStrength)
                      .ToList();
    }

    public async Task<(bool ok, string message)> ConnectAsync(string ssid, string? password)
    {
        try
        {
            var args = new List<string> { "device", "wifi", "connect", ssid };
            if (!string.IsNullOrEmpty(password))
            {
                args.Add("password");
                args.Add(password);
            }
            var output = await RunNmcliAsync(args.ToArray(), timeoutMs: 30000);

            if (output.Contains("successfully activated", StringComparison.OrdinalIgnoreCase))
            {
                return (true, "Connected");
            }
            // nmcli surfaces useful detail on the error line; pass it through.
            return (false, output.Trim());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<bool> DisconnectAsync()
    {
        try
        {
            var status = await GetCurrentSsidAsync();
            if (string.IsNullOrEmpty(status)) return true;
            await RunNmcliAsync(new[] { "connection", "down", status }, timeoutMs: 10000);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WiFiService.DisconnectAsync error: {ex.Message}");
            return false;
        }
    }

    public async Task<string?> GetCurrentSsidAsync()
    {
        try
        {
            var output = await RunNmcliAsync(new[]
            {
                "-t", "-f", "ACTIVE,SSID", "device", "wifi",
            }, timeoutMs: 5000);

            foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = SplitNmcliLine(rawLine);
                if (fields.Length >= 2 && fields[0] == "yes" && !string.IsNullOrWhiteSpace(fields[1]))
                {
                    return fields[1];
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WiFiService.GetCurrentSsidAsync error: {ex.Message}");
        }
        return null;
    }

    private static string[] SplitNmcliLine(string line)
    {
        // nmcli's -t output uses ':' as separator and escapes literal ':' as '\:'.
        // Simple parser that respects the escape.
        var fields = new List<string>();
        var current = new StringBuilder();
        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '\\' && i + 1 < line.Length && line[i + 1] == ':')
            {
                current.Append(':');
                i++;
            }
            else if (ch == ':')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }
        fields.Add(current.ToString());
        return fields.ToArray();
    }

    private static async Task<string> RunNmcliAsync(string[] args, int timeoutMs)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "nmcli",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start nmcli — is NetworkManager installed?");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(); } catch { }
            throw new TimeoutException($"nmcli {string.Join(' ', args)} timed out");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        // nmcli writes useful errors to stderr — merge so callers see the reason.
        return string.IsNullOrEmpty(stderr) ? stdout : stdout + stderr;
    }
}
