using FFmpeg.AutoGen;
using System.Runtime.InteropServices;
using AvaloniaMediaPlayer.Core.Utils;

namespace AvaloniaMediaPlayer.Core.Codecs;

/// <summary>
/// Audio codec for decoding audio frames (inspired by Kodi's CDVDAudioCodecFFmpeg)
/// </summary>
public unsafe class AudioCodec : IDisposable
{
    private AVCodecContext* _codecContext;
    private AVFrame* _frame;
    private SwrContext* _swrContext;
    private bool _isOpen;
    private BufferPool? _bufferPool; // Reuse audio buffers
    private int _timebaseNum = 1;
    private int _timebaseDen = 1000;

    public bool Open(StreamInfo streamInfo)
    {
        Close();

        var codec = ffmpeg.avcodec_find_decoder(streamInfo.CodecId);
        if (codec == null)
            return false;

        _codecContext = ffmpeg.avcodec_alloc_context3(codec);
        if (_codecContext == null)
            return false;

        _codecContext->ch_layout.nb_channels = streamInfo.Channels;
        _codecContext->sample_rate = streamInfo.SampleRate;
        _codecContext->sample_fmt = streamInfo.SampleFormat;

        // Set extradata if available
        if (streamInfo.ExtraData != null && streamInfo.ExtraData.Length > 0)
        {
            _codecContext->extradata = (byte*)ffmpeg.av_malloc((ulong)streamInfo.ExtraData.Length);
            _codecContext->extradata_size = streamInfo.ExtraData.Length;
            Marshal.Copy(streamInfo.ExtraData, 0, (IntPtr)_codecContext->extradata, streamInfo.ExtraData.Length);
        }

        var ret = ffmpeg.avcodec_open2(_codecContext, codec, null);
        if (ret < 0)
        {
            Close();
            return false;
        }

        _frame = ffmpeg.av_frame_alloc();

        // Initialize buffer pool for audio data (typical audio frame ~8KB for stereo 48kHz)
        // Use a larger size to handle any audio format safely
        int audioBufferSize = streamInfo.SampleRate * streamInfo.Channels * 2; // 1 second worth
        _bufferPool = new BufferPool(audioBufferSize);

        // Store timebase for PTS conversion
        _timebaseNum = streamInfo.TimebaseNum;
        _timebaseDen = streamInfo.TimebaseDen;

        _isOpen = true;
        return true;
    }

    public AudioFrame? DecodeFrame(AVPacket* packet)
    {
        if (!_isOpen || _codecContext == null || _frame == null)
            return null;

        var ret = ffmpeg.avcodec_send_packet(_codecContext, packet);
        if (ret < 0)
            return null;

        ret = ffmpeg.avcodec_receive_frame(_codecContext, _frame);
        if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
            return null;

        if (ret < 0)
            return null;

        // Convert to S16 planar format for playback
        var dstFormat = AVSampleFormat.AV_SAMPLE_FMT_S16;
        var dstChannels = _frame->ch_layout.nb_channels;
        var dstSampleRate = _frame->sample_rate;

        if (_swrContext == null)
        {
            SwrContext* swrCtx = ffmpeg.swr_alloc();

            AVChannelLayout srcLayout = _frame->ch_layout;
            AVChannelLayout dstLayout = _frame->ch_layout;

            AVChannelLayout* pSrcLayout = &srcLayout;
            AVChannelLayout* pDstLayout = &dstLayout;

            ffmpeg.swr_alloc_set_opts2(&swrCtx,
                pDstLayout, dstFormat, dstSampleRate,
                pSrcLayout, (AVSampleFormat)_frame->format, _frame->sample_rate,
                0, null);

            ffmpeg.swr_init(swrCtx);
            _swrContext = swrCtx;
        }

        var dstSamples = (int)ffmpeg.av_rescale_rnd(_frame->nb_samples, dstSampleRate, _frame->sample_rate, AVRounding.AV_ROUND_UP);
        var bufferSize = ffmpeg.av_samples_get_buffer_size(null, dstChannels, dstSamples, dstFormat, 1);
        var buffer = Marshal.AllocHGlobal(bufferSize);

        byte* dstData = (byte*)buffer;
        var convertedSamples = ffmpeg.swr_convert(_swrContext, &dstData, dstSamples,
            _frame->extended_data, _frame->nb_samples);

        if (convertedSamples < 0)
        {
            Marshal.FreeHGlobal(buffer);
            return null;
        }

        var actualBufferSize = ffmpeg.av_samples_get_buffer_size(null, dstChannels, convertedSamples, dstFormat, 1);

        // Rent buffer from pool instead of allocating
        var audioData = _bufferPool!.Rent();

        // Only copy what we need
        int copySize = Math.Min(actualBufferSize, audioData.Length);
        Marshal.Copy(buffer, audioData, 0, copySize);
        Marshal.FreeHGlobal(buffer);

        // Convert PTS to milliseconds
        long ptsMs = 0;
        if (_frame->pts != ffmpeg.AV_NOPTS_VALUE)
        {
            ptsMs = (_frame->pts * _timebaseNum * 1000) / _timebaseDen;
        }

        var audioFrame = new AudioFrame
        {
            Data = audioData,
            SampleCount = convertedSamples,
            Channels = dstChannels,
            SampleRate = dstSampleRate,
            PTS = _frame->pts != ffmpeg.AV_NOPTS_VALUE ? _frame->pts : 0,
            PTSMs = ptsMs,
            Duration = _frame->duration,
            BufferPool = _bufferPool,
            ActualSize = actualBufferSize
        };

        return audioFrame;
    }

    public void Flush()
    {
        if (_codecContext != null)
        {
            ffmpeg.avcodec_flush_buffers(_codecContext);
        }
    }

    public void Close()
    {
        if (_swrContext != null)
        {
            var swr = _swrContext;
            ffmpeg.swr_free(&swr);
            _swrContext = null;
        }

        if (_frame != null)
        {
            var frame = _frame;
            ffmpeg.av_frame_free(&frame);
            _frame = null;
        }

        if (_codecContext != null)
        {
            var ctx = _codecContext;
            ffmpeg.avcodec_free_context(&ctx);
            _codecContext = null;
        }

        _isOpen = false;
    }

    public void Dispose()
    {
        Close();
    }

    public bool IsOpen => _isOpen;
    public int Channels => _codecContext != null ? _codecContext->ch_layout.nb_channels : 0;
    public int SampleRate => _codecContext != null ? _codecContext->sample_rate : 0;
}

/// <summary>
/// Decoded audio frame data
/// </summary>
public class AudioFrame
{
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public int SampleCount { get; set; }
    public int Channels { get; set; }
    public int SampleRate { get; set; }
    public long PTS { get; set; }
    public long PTSMs { get; set; } // PTS converted to milliseconds
    public long Duration { get; set; }
    internal Utils.BufferPool? BufferPool { get; set; } // For returning buffer to pool
    public int ActualSize { get; set; } // Actual data size used

    /// <summary>
    /// Return the audio buffer to the pool for reuse
    /// </summary>
    public void ReturnBuffer()
    {
        if (BufferPool != null && Data != null)
        {
            BufferPool.Return(Data);
            Data = Array.Empty<byte>();
        }
    }
}
