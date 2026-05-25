using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace JellyTV.Services;

/// <summary>
/// Wraps systemctl for the openssh-server unit so the user can enable
/// remote access from inside the appliance for troubleshooting (no
/// desktop fallback). State changes need root; on the appliance the
/// jellytv user has passwordless sudo, on a dev box they don't and the
/// toggle just reports the failure.
/// </summary>
public class SshService
{
    private const string Unit = "ssh.service";

    public async Task<SshStatus> GetStatusAsync()
    {
        return new SshStatus
        {
            IsRunning = await SystemctlOkAsync("is-active", Unit),
            IsEnabledOnBoot = await SystemctlOkAsync("is-enabled", Unit),
        };
    }

    public async Task<bool> EnableAsync()
    {
        // enable for boot + start now — match what most users want from a
        // single "on" click. If either fails the caller refreshes status
        // and sees the actual end state.
        var ok1 = await SudoSystemctlAsync("enable", Unit);
        var ok2 = await SudoSystemctlAsync("start", Unit);
        return ok1 && ok2;
    }

    public async Task<bool> DisableAsync()
    {
        var ok1 = await SudoSystemctlAsync("stop", Unit);
        var ok2 = await SudoSystemctlAsync("disable", Unit);
        return ok1 && ok2;
    }

    /// <summary>
    /// First non-loopback IPv4 address — what the user needs after the
    /// toggle: `ssh jellytv@<this>`.
    /// </summary>
    public static string? GetLocalIp()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork
                            && !IPAddress.IsLoopback(a.Address))
                .Select(a => a.Address.ToString())
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SshService.GetLocalIp: {ex.Message}");
            return null;
        }
    }

    private static async Task<bool> SystemctlOkAsync(string subcommand, string unit)
    {
        // systemctl is-active / is-enabled return 0 if active/enabled,
        // non-zero otherwise. No sudo needed for read queries.
        var (exit, _) = await RunAsync("systemctl", new[] { subcommand, unit });
        return exit == 0;
    }

    private static async Task<bool> SudoSystemctlAsync(string subcommand, string unit)
    {
        // -n: never prompt for password. On the appliance jellytv has
        // NOPASSWD; on a dev box sudo refuses immediately, which is what
        // we want — better than hanging on a tty-less prompt.
        var (exit, output) = await RunAsync("sudo", new[] { "-n", "systemctl", subcommand, unit });
        if (exit != 0)
        {
            Console.WriteLine($"SshService: sudo systemctl {subcommand} {unit} exited {exit}: {output.Trim()}");
        }
        return exit == 0;
    }

    private static async Task<(int exitCode, string output)> RunAsync(string file, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null) return (-1, "");
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            var combined = stdout + (string.IsNullOrEmpty(stderr) ? "" : stderr);
            return (proc.ExitCode, combined);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}

public class SshStatus
{
    public bool IsRunning { get; set; }
    public bool IsEnabledOnBoot { get; set; }
}
