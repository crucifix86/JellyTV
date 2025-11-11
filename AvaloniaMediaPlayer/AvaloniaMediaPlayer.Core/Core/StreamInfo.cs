using FFmpeg.AutoGen;

namespace AvaloniaMediaPlayer.Core;

/// <summary>
/// Stream type enumeration (inspired by Kodi's StreamType)
/// </summary>
public enum StreamType
{
    None,
    Audio,
    Video,
    Subtitle
}

/// <summary>
/// Audio stream information (inspired by Kodi's AudioStreamInfo)
/// </summary>
public class AudioStreamInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Codec { get; set; } = string.Empty;
    public int Channels { get; set; }
    public int SampleRate { get; set; }
    public int Bitrate { get; set; }
}

/// <summary>
/// Video stream information (inspired by Kodi's VideoStreamInfo)
/// </summary>
public class VideoStreamInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Codec { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public double FrameRate { get; set; }
    public double AspectRatio { get; set; }
    public int Bitrate { get; set; }
}

/// <summary>
/// Subtitle stream information (inspired by Kodi's SubtitleStreamInfo)
/// </summary>
public class SubtitleStreamInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Codec { get; set; } = string.Empty;
    public bool IsForced { get; set; }
    public bool IsDefault { get; set; }
}

/// <summary>
/// Detailed stream information (inspired by Kodi's CDVDStreamInfo)
/// </summary>
public class StreamInfo
{
    public StreamType Type { get; set; }
    public AVCodecID CodecId { get; set; }
    public string CodecName { get; set; } = string.Empty;
    public byte[]? ExtraData { get; set; }

    // Video properties
    public int Width { get; set; }
    public int Height { get; set; }
    public double FrameRate { get; set; }
    public AVPixelFormat PixelFormat { get; set; }

    // Audio properties
    public int Channels { get; set; }
    public int SampleRate { get; set; }
    public AVSampleFormat SampleFormat { get; set; }

    // Common properties
    public long Bitrate { get; set; }
    public string Language { get; set; } = string.Empty;

    // Timebase for PTS conversion
    public int TimebaseNum { get; set; } = 1;
    public int TimebaseDen { get; set; } = 1000;

    public void Clear()
    {
        Type = StreamType.None;
        CodecId = AVCodecID.AV_CODEC_ID_NONE;
        CodecName = string.Empty;
        ExtraData = null;
        Width = 0;
        Height = 0;
        FrameRate = 0;
        Channels = 0;
        SampleRate = 0;
        Bitrate = 0;
        Language = string.Empty;
    }
}
