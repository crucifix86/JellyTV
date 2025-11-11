namespace AvaloniaMediaPlayer.Core.Overlay;

/// <summary>
/// Overlay types (inspired by Kodi's DVDOverlayType)
/// </summary>
public enum OverlayType
{
    None = -1,
    Text = 2,
    Image = 3,
    SSA = 4
}

/// <summary>
/// Base overlay class with PTS synchronization (inspired by Kodi's CDVDOverlay)
/// </summary>
public class DVDOverlay
{
    public OverlayType Type { get; set; }

    /// <summary>
    /// Presentation timestamp start time in seconds
    /// </summary>
    public double PTSStart { get; set; }

    /// <summary>
    /// Presentation timestamp stop time in seconds
    /// </summary>
    public double PTSStop { get; set; }

    /// <summary>
    /// Force display regardless of subtitle visibility settings
    /// </summary>
    public bool IsForced { get; set; }

    /// <summary>
    /// Replace with next overlay regardless of stop time
    /// </summary>
    public bool Replace { get; set; }

    /// <summary>
    /// Enable text alignment
    /// </summary>
    public bool TextAlignEnabled { get; set; }

    /// <summary>
    /// Allow overlay container to flush this overlay
    /// </summary>
    public bool IsFlushable { get; set; } = true;

    public DVDOverlay(OverlayType type)
    {
        Type = type;
        PTSStart = 0;
        PTSStop = 0;
        IsForced = false;
        Replace = false;
    }

    public virtual DVDOverlay Clone()
    {
        return (DVDOverlay)MemberwiseClone();
    }
}

/// <summary>
/// Text overlay for subtitles (inspired by Kodi's CDVDOverlayText)
/// </summary>
public class DVDOverlayText : DVDOverlay
{
    public string Text { get; set; } = string.Empty;
    public TextAlignment Alignment { get; set; } = TextAlignment.Bottom;

    public DVDOverlayText() : base(OverlayType.Text)
    {
    }
}

/// <summary>
/// Image overlay for bitmap subtitles (inspired by Kodi's CDVDOverlayImage)
/// </summary>
public class DVDOverlayImage : DVDOverlay
{
    public byte[]? ImageData { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int X { get; set; }
    public int Y { get; set; }

    public DVDOverlayImage() : base(OverlayType.Image)
    {
    }
}

/// <summary>
/// SSA/ASS subtitle overlay (inspired by Kodi's CDVDOverlaySSA)
/// </summary>
public class DVDOverlaySSA : DVDOverlay
{
    public string Event { get; set; } = string.Empty;
    public string Style { get; set; } = string.Empty;

    public DVDOverlaySSA() : base(OverlayType.SSA)
    {
    }
}

public enum TextAlignment
{
    Top,
    Center,
    Bottom
}
