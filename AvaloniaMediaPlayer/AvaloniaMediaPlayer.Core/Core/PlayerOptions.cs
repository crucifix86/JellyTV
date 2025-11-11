namespace AvaloniaMediaPlayer.Core;

/// <summary>
/// Player options for opening media (inspired by Kodi's CPlayerOptions)
/// </summary>
public class PlayerOptions
{
    /// <summary>
    /// Start time in seconds
    /// </summary>
    public double StartTime { get; set; } = 0;

    /// <summary>
    /// Start position as percentage (0-100)
    /// </summary>
    public double StartPercent { get; set; } = 0;

    /// <summary>
    /// Start in fullscreen mode
    /// </summary>
    public bool Fullscreen { get; set; } = false;

    /// <summary>
    /// Play video only (no audio)
    /// </summary>
    public bool VideoOnly { get; set; } = false;

    /// <summary>
    /// Prefer stereo audio streams
    /// </summary>
    public bool PreferStereo { get; set; } = false;

    /// <summary>
    /// Player state to restore (for resume)
    /// </summary>
    public string State { get; set; } = string.Empty;
}
