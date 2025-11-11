using FFmpeg.AutoGen;
using System.Runtime.InteropServices;

namespace AvaloniaMediaPlayer.Core.Demux;

/// <summary>
/// FFmpeg-based demuxer for reading media files (inspired by Kodi's DVDDemuxFFmpeg)
/// </summary>
public unsafe class FFmpegDemuxer : IDisposable
{
    private AVFormatContext* _formatContext;
    private bool _isOpen;
    private readonly Dictionary<int, StreamInfo> _streams = new();

    static FFmpegDemuxer()
    {
        // Register FFmpeg libraries
        ffmpeg.RootPath = GetFFmpegPath();
    }

    private static string GetFFmpegPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "/usr/lib/x86_64-linux-gnu";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Environment.CurrentDirectory;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "/usr/local/lib";
        }
        return Environment.CurrentDirectory;
    }

    public bool Open(string filePath)
    {
        Close();

        AVFormatContext* fmt = null;
        var ret = ffmpeg.avformat_open_input(&fmt, filePath, null, null);
        if (ret < 0)
        {
            return false;
        }

        _formatContext = fmt;

        ret = ffmpeg.avformat_find_stream_info(_formatContext, null);
        if (ret < 0)
        {
            Close();
            return false;
        }

        // Parse all streams
        for (int i = 0; i < _formatContext->nb_streams; i++)
        {
            var stream = _formatContext->streams[i];
            var codecParams = stream->codecpar;

            var streamInfo = new StreamInfo();

            if (codecParams->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
            {
                streamInfo.Type = StreamType.Video;
                streamInfo.Width = codecParams->width;
                streamInfo.Height = codecParams->height;
                streamInfo.PixelFormat = (AVPixelFormat)codecParams->format;
                streamInfo.FrameRate = (double)stream->avg_frame_rate.num / stream->avg_frame_rate.den;
                streamInfo.TimebaseNum = stream->time_base.num;
                streamInfo.TimebaseDen = stream->time_base.den;
            }
            else if (codecParams->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
            {
                streamInfo.Type = StreamType.Audio;
                streamInfo.Channels = codecParams->ch_layout.nb_channels;
                streamInfo.SampleRate = codecParams->sample_rate;
                streamInfo.SampleFormat = (AVSampleFormat)codecParams->format;
            }
            else if (codecParams->codec_type == AVMediaType.AVMEDIA_TYPE_SUBTITLE)
            {
                streamInfo.Type = StreamType.Subtitle;
            }
            else
            {
                continue;
            }

            streamInfo.CodecId = codecParams->codec_id;
            var codec = ffmpeg.avcodec_find_decoder(codecParams->codec_id);
            if (codec != null)
            {
                streamInfo.CodecName = Marshal.PtrToStringAnsi((IntPtr)codec->name) ?? "";
            }
            streamInfo.Bitrate = codecParams->bit_rate;

            // Copy extradata if present
            if (codecParams->extradata != null && codecParams->extradata_size > 0)
            {
                streamInfo.ExtraData = new byte[codecParams->extradata_size];
                Marshal.Copy((IntPtr)codecParams->extradata, streamInfo.ExtraData, 0, codecParams->extradata_size);
            }

            _streams[i] = streamInfo;
        }

        _isOpen = true;
        return true;
    }

    public AVPacket* ReadPacket()
    {
        if (!_isOpen || _formatContext == null)
            return null;

        var packet = ffmpeg.av_packet_alloc();
        var ret = ffmpeg.av_read_frame(_formatContext, packet);

        if (ret < 0)
        {
            ffmpeg.av_packet_free(&packet);
            return null;
        }

        return packet;
    }

    public void FreePacket(AVPacket* packet)
    {
        if (packet != null)
        {
            ffmpeg.av_packet_free(&packet);
        }
    }

    public bool IsKeyFrame(AVPacket* packet)
    {
        if (packet == null)
            return false;

        return (packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0;
    }

    public bool Seek(long timestampMs, bool keyframeOnly = true)
    {
        if (!_isOpen || _formatContext == null)
            return false;

        // Find video stream for seeking
        var videoStreams = GetStreamIndices(StreamType.Video);
        int streamIndex = videoStreams.Count > 0 ? videoStreams[0] : -1;

        if (streamIndex >= 0)
        {
            // Convert timestamp to stream timebase
            var stream = _formatContext->streams[streamIndex];
            long timestamp = ffmpeg.av_rescale_q(
                timestampMs * 1000, // microseconds
                new AVRational { num = 1, den = 1000000 }, // microsecond timebase
                stream->time_base
            );

            // Seek flags: BACKWARD ensures we land on or before a keyframe
            // This is critical for proper decoding since we can only decode from keyframes
            int seekFlags = ffmpeg.AVSEEK_FLAG_BACKWARD;

            // If keyframe-only, we accept landing on the keyframe even if it's earlier
            // Otherwise, we'll decode from the keyframe to the exact position (slower)
            if (!keyframeOnly)
            {
                seekFlags |= ffmpeg.AVSEEK_FLAG_ANY;
            }

            var ret = ffmpeg.av_seek_frame(_formatContext, streamIndex, timestamp, seekFlags);
            return ret >= 0;
        }
        else
        {
            // No video stream, use generic seek
            long timestamp = timestampMs * 1000; // Convert to microseconds
            var ret = ffmpeg.av_seek_frame(_formatContext, -1, timestamp, ffmpeg.AVSEEK_FLAG_BACKWARD);
            return ret >= 0;
        }
    }

    public long GetDuration()
    {
        if (!_isOpen || _formatContext == null)
            return 0;

        return _formatContext->duration / 1000; // Convert from microseconds to milliseconds
    }

    public Dictionary<int, StreamInfo> GetStreams()
    {
        return new Dictionary<int, StreamInfo>(_streams);
    }

    public StreamInfo? GetStream(int index)
    {
        return _streams.TryGetValue(index, out var stream) ? stream : null;
    }

    public int GetStreamCount(StreamType type)
    {
        return _streams.Values.Count(s => s.Type == type);
    }

    public List<int> GetStreamIndices(StreamType type)
    {
        return _streams.Where(kvp => kvp.Value.Type == type).Select(kvp => kvp.Key).ToList();
    }

    public void Close()
    {
        if (_formatContext != null)
        {
            var fmt = _formatContext;
            ffmpeg.avformat_close_input(&fmt);
            _formatContext = null;
        }
        _streams.Clear();
        _isOpen = false;
    }

    public void Dispose()
    {
        Close();
    }

    public long GetVideoTimeBase()
    {
        if (!_isOpen || _formatContext == null)
            return 1000; // Default to milliseconds

        var videoStreams = GetStreamIndices(StreamType.Video);
        if (videoStreams.Count == 0)
            return 1000;

        var stream = _formatContext->streams[videoStreams[0]];
        if (stream != null && stream->time_base.den > 0)
        {
            return stream->time_base.den / stream->time_base.num;
        }

        return 1000;
    }

    public bool IsOpen => _isOpen;
}
