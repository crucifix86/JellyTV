using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace JellyTV.Services;

public class VideoOutput
{
    public string Name { get; set; } = "";
    public string Resolution { get; set; } = "";
    public bool IsConnected { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; }
    public List<string> SupportedResolutions { get; set; } = new();
}

public class VideoService
{
    public List<VideoOutput> GetVideoOutputs()
    {
        var outputs = new List<VideoOutput>();

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "xrandr",
                    Arguments = "--query",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            Console.WriteLine("xrandr output:");
            Console.WriteLine(output);

            // Parse xrandr output
            var lines = output.Split('\n');
            VideoOutput? currentOutput = null;

            foreach (var line in lines)
            {
                // Check if this is an output line (e.g., "HDMI-1 connected 1920x1080+0+0")
                var outputMatch = Regex.Match(line, @"^(\S+)\s+(connected|disconnected)(.*)");
                if (outputMatch.Success)
                {
                    var outputName = outputMatch.Groups[1].Value;
                    var connected = outputMatch.Groups[2].Value == "connected";
                    var rest = outputMatch.Groups[3].Value;

                    currentOutput = new VideoOutput
                    {
                        Name = outputName,
                        IsConnected = connected,
                        IsPrimary = rest.Contains("primary")
                    };

                    // Check if it's currently active and get resolution
                    var resMatch = Regex.Match(rest, @"(\d+x\d+)\+");
                    if (resMatch.Success)
                    {
                        currentOutput.IsActive = true;
                        currentOutput.Resolution = resMatch.Groups[1].Value;
                    }
                    else
                    {
                        currentOutput.Resolution = "Not active";
                    }

                    outputs.Add(currentOutput);
                    Console.WriteLine($"Found video output: {outputName}, Connected: {connected}, Active: {currentOutput.IsActive}, Resolution: {currentOutput.Resolution}");
                }
                // Parse available resolutions for the current output
                else if (currentOutput != null && line.Trim().StartsWith(" "))
                {
                    var resMatch = Regex.Match(line.Trim(), @"^(\d+x\d+)");
                    if (resMatch.Success)
                    {
                        var resolution = resMatch.Groups[1].Value;
                        currentOutput.SupportedResolutions.Add(resolution);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting video outputs: {ex.Message}");
        }

        return outputs.Where(o => o.IsConnected).ToList();
    }

    public bool SetVideoOutput(string outputName, string resolution)
    {
        try
        {
            Console.WriteLine($"Setting video output: {outputName} at {resolution}");

            // Turn off all other outputs and enable this one
            var allOutputs = GetVideoOutputs();
            var args = "";

            foreach (var output in allOutputs)
            {
                if (output.Name == outputName)
                {
                    // Enable this output with the specified resolution
                    args += $"--output {output.Name} --mode {resolution} --primary ";
                }
                else if (output.IsActive)
                {
                    // Turn off other active outputs
                    args += $"--output {output.Name} --off ";
                }
            }

            Console.WriteLine($"xrandr command: xrandr {args}");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "xrandr",
                    Arguments = args.Trim(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                Console.WriteLine($"Successfully set video output to: {outputName} at {resolution}");
                return true;
            }
            else
            {
                Console.WriteLine($"Failed to set video output: {stderr}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting video output: {ex.Message}");
            return false;
        }
    }

    public bool EnableVideoOutput(string outputName, bool enable)
    {
        try
        {
            var args = enable
                ? $"--output {outputName} --auto"
                : $"--output {outputName} --off";

            Console.WriteLine($"xrandr command: xrandr {args}");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "xrandr",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                Console.WriteLine($"Successfully {(enable ? "enabled" : "disabled")} output: {outputName}");
                return true;
            }
            else
            {
                Console.WriteLine($"Failed to change output: {stderr}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error changing video output: {ex.Message}");
            return false;
        }
    }
}
