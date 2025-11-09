using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace JellyTV.Services;

public class AudioDevice
{
    public string Name { get; set; } = "";
    public string Id { get; set; } = "";
    public bool IsDefault { get; set; }
}

public class AudioService
{
    public List<AudioDevice> GetAudioDevices()
    {
        var devices = new List<AudioDevice>();

        try
        {
            // Use pactl to list audio sinks (output devices)
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pactl",
                    Arguments = "list sinks",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // Get default sink
            var defaultSink = GetDefaultSink();
            Console.WriteLine($"Default sink: {defaultSink}");

            // Parse pactl output
            var sinkBlocks = output.Split(new[] { "Sink #" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var block in sinkBlocks)
            {
                var nameMatch = Regex.Match(block, @"Name:\s*(.+)");
                var descMatch = Regex.Match(block, @"Description:\s*(.+)");

                if (nameMatch.Success && descMatch.Success)
                {
                    var sinkName = nameMatch.Groups[1].Value.Trim();
                    var description = descMatch.Groups[1].Value.Trim();

                    devices.Add(new AudioDevice
                    {
                        Id = sinkName,
                        Name = description,
                        IsDefault = sinkName == defaultSink
                    });

                    Console.WriteLine($"Found audio device: {description} ({sinkName})");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting audio devices: {ex.Message}");
        }

        return devices;
    }

    private string GetDefaultSink()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pactl",
                    Arguments = "get-default-sink",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            return output;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting default sink: {ex.Message}");
            return "";
        }
    }

    public bool SetDefaultAudioDevice(string sinkName)
    {
        try
        {
            Console.WriteLine($"Setting default audio device to: {sinkName}");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pactl",
                    Arguments = $"set-default-sink {sinkName}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                Console.WriteLine($"Successfully set default audio device to: {sinkName}");

                // Also move all existing streams to the new sink
                MoveAllStreamsToSink(sinkName);

                return true;
            }
            else
            {
                Console.WriteLine($"Failed to set audio device: {error}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting audio device: {ex.Message}");
            return false;
        }
    }

    private void MoveAllStreamsToSink(string sinkName)
    {
        try
        {
            // Get all sink inputs (playing streams)
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pactl",
                    Arguments = "list short sink-inputs",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // Parse sink input IDs
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split('\t');
                if (parts.Length > 0)
                {
                    var inputId = parts[0];

                    // Move this input to the new sink
                    var moveProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "pactl",
                            Arguments = $"move-sink-input {inputId} {sinkName}",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    moveProcess.Start();
                    moveProcess.WaitForExit();

                    Console.WriteLine($"Moved stream {inputId} to {sinkName}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error moving streams: {ex.Message}");
        }
    }
}
