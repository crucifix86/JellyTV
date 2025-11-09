using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace JellyTV.Services;

public class BluetoothDevice
{
    public string Address { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsPaired { get; set; }
    public bool IsConnected { get; set; }
    public string DeviceType { get; set; } = "Unknown";
}

public class BluetoothService
{
    private Process? _scanProcess;
    private bool _isScanning;

    public async Task<bool> PowerOnBluetoothAsync()
    {
        try
        {
            Console.WriteLine("Powering on Bluetooth adapter...");
            var result = await RunBluetoothCtlCommandAsync("power on");
            return result.Contains("succeeded") || result.Contains("Changing power on succeeded");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error powering on Bluetooth: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> StartScanAsync()
    {
        try
        {
            if (_isScanning)
                return true;

            Console.WriteLine("Starting Bluetooth scan...");

            // Make sure Bluetooth is powered on
            await PowerOnBluetoothAsync();

            // Start scanning
            var result = await RunBluetoothCtlCommandAsync("scan on");
            _isScanning = true;

            Console.WriteLine("Bluetooth scan started");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting scan: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> StopScanAsync()
    {
        try
        {
            if (!_isScanning)
                return true;

            Console.WriteLine("Stopping Bluetooth scan...");
            await RunBluetoothCtlCommandAsync("scan off");
            _isScanning = false;

            Console.WriteLine("Bluetooth scan stopped");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping scan: {ex.Message}");
            return false;
        }
    }

    public async Task<List<BluetoothDevice>> GetDevicesAsync()
    {
        var devices = new List<BluetoothDevice>();

        try
        {
            // Use bluetoothctl devices to get all devices from bluetoothd daemon
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bluetoothctl",
                    Arguments = "devices",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            Console.WriteLine($"bluetoothctl -- devices output:\n{output}");

            // Parse device list
            // Format: "Device XX:XX:XX:XX:XX:XX Device Name"
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                // Remove ANSI escape codes that bluetoothctl might add
                var cleanLine = Regex.Replace(line, @"\x1B\[[^@-~]*[@-~]", "");

                var match = Regex.Match(cleanLine, @"Device\s+([0-9A-Fa-f:]+)\s+(.+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var address = match.Groups[1].Value;
                    var name = match.Groups[2].Value.Trim();

                    // Get device info
                    var info = await GetDeviceInfoAsync(address);

                    devices.Add(new BluetoothDevice
                    {
                        Address = address,
                        Name = name,
                        IsPaired = info.IsPaired,
                        IsConnected = info.IsConnected,
                        DeviceType = info.DeviceType
                    });

                    Console.WriteLine($"Found device: {name} ({address}) - Paired: {info.IsPaired}, Connected: {info.IsConnected}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting devices: {ex.Message}");
        }

        return devices;
    }

    public async Task<List<BluetoothDevice>> GetPairedDevicesAsync()
    {
        var devices = new List<BluetoothDevice>();

        try
        {
            // Use "devices" command and filter for paired devices
            var output = await RunBluetoothCtlCommandAsync("devices");

            // Parse device list
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                // Remove ANSI escape codes that bluetoothctl might add
                var cleanLine = Regex.Replace(line, @"\x1B\[[^@-~]*[@-~]", "");

                var match = Regex.Match(cleanLine, @"Device\s+([0-9A-Fa-f:]+)\s+(.+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var address = match.Groups[1].Value;
                    var name = match.Groups[2].Value.Trim();

                    var info = await GetDeviceInfoAsync(address);

                    // Only include paired devices
                    if (info.IsPaired)
                    {
                        devices.Add(new BluetoothDevice
                        {
                            Address = address,
                            Name = name,
                            IsPaired = true,
                            IsConnected = info.IsConnected,
                            DeviceType = info.DeviceType
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting paired devices: {ex.Message}");
        }

        return devices;
    }

    private async Task<BluetoothDevice> GetDeviceInfoAsync(string address)
    {
        var device = new BluetoothDevice { Address = address };

        try
        {
            var output = await RunBluetoothCtlCommandAsync($"info {address}");

            device.IsPaired = output.Contains("Paired: yes");
            device.IsConnected = output.Contains("Connected: yes");

            // Try to determine device type
            if (output.Contains("Icon: audio-card") || output.Contains("Icon: audio-headset"))
                device.DeviceType = "Audio";
            else if (output.Contains("Icon: input-gaming"))
                device.DeviceType = "Controller";
            else if (output.Contains("Icon: input-keyboard"))
                device.DeviceType = "Keyboard";
            else if (output.Contains("Icon: input-mouse"))
                device.DeviceType = "Mouse";

            var nameMatch = Regex.Match(output, @"Name:\s+(.+)");
            if (nameMatch.Success)
                device.Name = nameMatch.Groups[1].Value.Trim();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting device info for {address}: {ex.Message}");
        }

        return device;
    }

    public async Task<bool> PairDeviceAsync(string address)
    {
        try
        {
            Console.WriteLine($"Pairing with device {address}...");

            // We need to keep bluetoothctl running with an agent to handle pairing
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bluetoothctl",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            var output = "";
            var outputLock = new object();

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    lock (outputLock)
                    {
                        output += e.Data + "\n";
                    }
                    Console.WriteLine($"bluetoothctl: {e.Data}");
                }
            };

            process.Start();
            process.BeginOutputReadLine();

            // Small delay to let bluetoothctl start
            await Task.Delay(500);

            // Register agent to handle pairing
            await process.StandardInput.WriteLineAsync("agent on");
            await Task.Delay(200);
            await process.StandardInput.WriteLineAsync("default-agent");
            await Task.Delay(200);

            // Trust the device first
            await process.StandardInput.WriteLineAsync($"trust {address}");
            await Task.Delay(500);

            // Start pairing
            await process.StandardInput.WriteLineAsync($"pair {address}");

            // Wait for pairing to complete (up to 30 seconds)
            var startTime = DateTime.Now;
            var success = false;

            while ((DateTime.Now - startTime).TotalSeconds < 30)
            {
                await Task.Delay(500);

                string currentOutput;
                lock (outputLock)
                {
                    currentOutput = output;
                }

                if (currentOutput.Contains("Pairing successful") || currentOutput.Contains("already paired"))
                {
                    success = true;
                    Console.WriteLine($"Paired successfully with {address}");
                    break;
                }
                else if (currentOutput.Contains("Failed to pair") || currentOutput.Contains("org.bluez.Error"))
                {
                    Console.WriteLine($"Pairing failed: {currentOutput}");
                    break;
                }
            }

            // Clean up
            await process.StandardInput.WriteLineAsync("exit");
            await process.StandardInput.FlushAsync();

            try
            {
                await process.WaitForExitAsync(new CancellationToken());
            }
            catch
            {
                process.Kill();
            }

            return success;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error pairing device: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ConnectDeviceAsync(string address)
    {
        try
        {
            Console.WriteLine($"Connecting to device {address}...");
            var result = await RunBluetoothCtlCommandAsync($"connect {address}", timeout: 15000);

            if (result.Contains("Connection successful"))
            {
                Console.WriteLine($"Connected successfully to {address}");
                return true;
            }
            else
            {
                Console.WriteLine($"Connection failed: {result}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error connecting device: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DisconnectDeviceAsync(string address)
    {
        try
        {
            Console.WriteLine($"Disconnecting from device {address}...");
            var result = await RunBluetoothCtlCommandAsync($"disconnect {address}");

            Console.WriteLine($"Disconnected from {address}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error disconnecting device: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> RemoveDeviceAsync(string address)
    {
        try
        {
            Console.WriteLine($"Removing device {address}...");
            var result = await RunBluetoothCtlCommandAsync($"remove {address}");

            Console.WriteLine($"Removed device {address}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error removing device: {ex.Message}");
            return false;
        }
    }

    private async Task<string> RunBluetoothCtlCommandAsync(string command, int timeout = 5000)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "bluetoothctl",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var output = "";
        var outputComplete = new TaskCompletionSource<bool>();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                output += e.Data + "\n";
            }
        };

        process.Start();
        process.BeginOutputReadLine();

        // Send command
        await process.StandardInput.WriteLineAsync(command);

        // Wait a bit for commands that modify state (remove, disconnect, connect, pair)
        if (command.StartsWith("remove ") || command.StartsWith("disconnect ") ||
            command.StartsWith("connect ") || command.StartsWith("pair "))
        {
            await Task.Delay(1000); // Give the command time to complete
        }

        await process.StandardInput.WriteLineAsync("exit");
        await process.StandardInput.FlushAsync();

        // Wait for process to complete or timeout
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill();
            Console.WriteLine($"Command '{command}' timed out");
        }

        return output;
    }
}
