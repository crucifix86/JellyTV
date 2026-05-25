using FFmpeg.AutoGen;
using System.Runtime.InteropServices;
using System.Diagnostics;
using AvaloniaMediaPlayer.Core.Utils;

namespace AvaloniaMediaPlayer.Core.Codecs;

/// <summary>
/// Video codec for decoding video frames with hardware acceleration support (inspired by Kodi's CDVDVideoCodecFFmpeg)
/// </summary>
public unsafe class VideoCodec : IDisposable
{
    private AVCodecContext* _codecContext;
    private AVFrame* _frame;
    private AVFrame* _hwFrame;
    private SwsContext* _swsContext;
    private AVBufferRef* _hwDeviceCtx;
    private bool _isOpen;
    private bool _hwAccelEnabled;
    private double _timebaseToMilliseconds = 1.0;
    private int _frameCount = 0; // For diagnostic logging
    private BufferPool? _bufferPool; // Reuse buffers to reduce GC pressure

    public bool EnableHardwareAcceleration { get; set; } = true;

    public bool Open(StreamInfo streamInfo)
    {
        Close();

        var codec = ffmpeg.avcodec_find_decoder(streamInfo.CodecId);
        if (codec == null)
        {
            Console.WriteLine($"ERROR: Failed to find decoder for codec ID: {streamInfo.CodecId}");
            return false;
        }

        var codecName = Marshal.PtrToStringAnsi((IntPtr)codec->name);
        Console.WriteLine($"Opening video codec: {codecName} ({streamInfo.Width}x{streamInfo.Height})");

        _codecContext = ffmpeg.avcodec_alloc_context3(codec);
        if (_codecContext == null)
        {
            Console.WriteLine("ERROR: Failed to allocate codec context");
            return false;
        }

        _codecContext->width = streamInfo.Width;
        _codecContext->height = streamInfo.Height;
        _codecContext->pix_fmt = streamInfo.PixelFormat;

        // Set extradata if available (critical for H.264/HEVC)
        if (streamInfo.ExtraData != null && streamInfo.ExtraData.Length > 0)
        {
            _codecContext->extradata = (byte*)ffmpeg.av_malloc((ulong)streamInfo.ExtraData.Length);
            _codecContext->extradata_size = streamInfo.ExtraData.Length;
            Marshal.Copy(streamInfo.ExtraData, 0, (IntPtr)_codecContext->extradata, streamInfo.ExtraData.Length);
            Console.WriteLine($"Set extradata: {streamInfo.ExtraData.Length} bytes");
        }

        // Try to enable hardware acceleration
        if (EnableHardwareAcceleration)
        {
            var hwAccelType = HardwareAcceleration.DetectBestAcceleration();
            Console.WriteLine($"Attempting hardware acceleration: {hwAccelType}");
            _hwAccelEnabled = HardwareAcceleration.TryCreateHWContext(_codecContext, hwAccelType, out _hwDeviceCtx);

            if (_hwAccelEnabled)
            {
                _hwFrame = ffmpeg.av_frame_alloc();
                Console.WriteLine("Hardware acceleration context created successfully");
            }
            else
            {
                Console.WriteLine("Hardware acceleration failed, using software decoding");
            }
        }

        var ret = ffmpeg.avcodec_open2(_codecContext, codec, null);
        if (ret < 0)
        {
            Console.WriteLine($"ERROR: Failed to open codec: {ret}");
            Close();
            return false;
        }

        Console.WriteLine($"Codec opened successfully. Pixel format: {_codecContext->pix_fmt}");

        _frame = ffmpeg.av_frame_alloc();

        // Calculate timebase to milliseconds conversion factor
        // Formula: (timebase_num * 1000) / timebase_den
        // Example: timebase 1/90000 -> (1 * 1000) / 90000 = 0.0111...
        if (streamInfo.TimebaseDen > 0)
        {
            _timebaseToMilliseconds = (streamInfo.TimebaseNum * 1000.0) / streamInfo.TimebaseDen;
            Console.WriteLine($"Timebase: {streamInfo.TimebaseNum}/{streamInfo.TimebaseDen} (conversion: {_timebaseToMilliseconds}ms per unit)");
        }

        // Initialize buffer pool for frame data to reduce GC pressure
        // Calculate buffer size for BGRA format
        int bufferSize = streamInfo.Width * streamInfo.Height * 4; // 4 bytes per pixel (BGRA)
        _bufferPool = new BufferPool(bufferSize);
        Console.WriteLine($"Initialized buffer pool with {bufferSize} byte buffers ({bufferSize / 1024}KB per frame)");

        _isOpen = true;
        return true;
    }

    public VideoFrame? DecodeFrame(AVPacket* packet)
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
        {
            if (_frameCount < 10) // Log first few errors
                Console.WriteLine($"ERROR: Failed to receive frame: {ret}");
            return null;
        }

        _frameCount++;

        // Log first few frames for diagnostics
        if (_frameCount <= 5)
        {
            Console.WriteLine($"Frame {_frameCount}: format={(AVPixelFormat)_frame->format}, size={_frame->width}x{_frame->height}, pts={_frame->pts}, hw_accel={_hwAccelEnabled}");
        }

        AVFrame* frameToConvert = _frame;

        // If hardware acceleration is enabled, check if frame is in hardware format
        if (_hwAccelEnabled && IsHardwarePixelFormat((AVPixelFormat)_frame->format))
        {
            if (_hwFrame == null)
                _hwFrame = ffmpeg.av_frame_alloc();

            // Transfer data from GPU to CPU memory
            // av_hwframe_transfer_data auto-detects the correct format (NV12, P010LE, etc.)
            var sw = Stopwatch.StartNew();
            ret = ffmpeg.av_hwframe_transfer_data(_hwFrame, _frame, 0);
            sw.Stop();

            if (_frameCount <= 10)
                Console.WriteLine($"HW transfer time: {sw.Elapsed.TotalMilliseconds:F2}ms");

            if (ret < 0)
            {
                // Hardware transfer failed - this is critical, we can't render GPU memory directly
                Console.WriteLine($"ERROR: Failed to transfer hardware frame (format: {(AVPixelFormat)_frame->format}), error code: {ret}");
                return null; // Return null instead of trying to use GPU memory
            }

            // Copy frame properties
            ffmpeg.av_frame_copy_props(_hwFrame, _frame);

            // Force colorspace settings for proper YUV->RGB conversion
            if (_hwFrame->height >= 720)
            {
                _hwFrame->colorspace = AVColorSpace.AVCOL_SPC_BT709;
            }
            else
            {
                _hwFrame->colorspace = AVColorSpace.AVCOL_SPC_SMPTE170M;
            }
            _hwFrame->color_range = AVColorRange.AVCOL_RANGE_MPEG;

            frameToConvert = _hwFrame;
        }

        // Convert to BGRA format for Avalonia rendering
        var dstFormat = AVPixelFormat.AV_PIX_FMT_BGRA;
        var dstWidth = frameToConvert->width;
        var dstHeight = frameToConvert->height;

        // Create swsContext if it doesn't exist
        if (_swsContext == null)
        {
            // Use high quality scaling with proper color space handling
            // SWS_BICUBIC gives better quality than BILINEAR for YUV->RGB conversion
            int flags = ffmpeg.SWS_BICUBIC | ffmpeg.SWS_FULL_CHR_H_INT | ffmpeg.SWS_ACCURATE_RND;

            _swsContext = ffmpeg.sws_getContext(
                frameToConvert->width, frameToConvert->height, (AVPixelFormat)frameToConvert->format,
                dstWidth, dstHeight, dstFormat,
                flags, null, null, null);

            if (_swsContext != null)
            {
                // Set proper color space conversion for YUV->RGB
                // Use frame's colorspace if specified, otherwise infer from resolution
                var colorspace = frameToConvert->colorspace;
                if (colorspace == AVColorSpace.AVCOL_SPC_UNSPECIFIED)
                {
                    colorspace = frameToConvert->height >= 720 ? AVColorSpace.AVCOL_SPC_BT709 : AVColorSpace.AVCOL_SPC_SMPTE170M;
                }

                var srcCoeffs = ffmpeg.sws_getCoefficients((int)colorspace);
                var dstCoeffs = ffmpeg.sws_getCoefficients((int)AVColorSpace.AVCOL_SPC_RGB);

                // Use frame's color range if specified, otherwise assume limited range
                int srcRange = frameToConvert->color_range == AVColorRange.AVCOL_RANGE_JPEG ? 1 : 0;
                int dstRange = 1;  // 1 = full range (0-255)

                // Copy coefficients to int_array4 structures
                var srcTable = new int_array4();
                var dstTable = new int_array4();

                for (uint i = 0; i < 4; i++)
                {
                    srcTable[i] = srcCoeffs[i];
                    dstTable[i] = dstCoeffs[i];
                }

                // Apply color space conversion parameters
                ffmpeg.sws_setColorspaceDetails(_swsContext, srcTable, srcRange, dstTable, dstRange, 0, 1 << 16, 1 << 16);
            }
        }

        var dstData = new byte_ptrArray4();
        var dstLinesize = new int_array4();

        var bufferSize = ffmpeg.av_image_get_buffer_size(dstFormat, dstWidth, dstHeight, 1);
        var buffer = Marshal.AllocHGlobal(bufferSize);

        ffmpeg.av_image_fill_arrays(ref dstData, ref dstLinesize, (byte*)buffer, dstFormat, dstWidth, dstHeight, 1);

        // Measure color conversion time
        var swsTime = Stopwatch.StartNew();
        ffmpeg.sws_scale(_swsContext, frameToConvert->data, frameToConvert->linesize, 0, frameToConvert->height, dstData, dstLinesize);
        swsTime.Stop();

        if (_frameCount <= 10)
            Console.WriteLine($"Color conversion time: {swsTime.Elapsed.TotalMilliseconds:F2}ms");

        // Rent buffer from pool instead of allocating
        var copyTime = Stopwatch.StartNew();
        var imageData = _bufferPool!.Rent();
        Marshal.Copy(buffer, imageData, 0, bufferSize);
        Marshal.FreeHGlobal(buffer);
        copyTime.Stop();

        if (_frameCount <= 10)
            Console.WriteLine($"Memory copy time: {copyTime.Elapsed.TotalMilliseconds:F2}ms, Total frame time: {(swsTime.Elapsed.TotalMilliseconds + copyTime.Elapsed.TotalMilliseconds):F2}ms, Pool size: {_bufferPool.PooledCount}");

        // Convert PTS from stream timebase to milliseconds
        long ptsMs = 0;
        if (_frame->pts != ffmpeg.AV_NOPTS_VALUE)
        {
            ptsMs = (long)(_frame->pts * _timebaseToMilliseconds);
        }

        var videoFrame = new VideoFrame
        {
            Data = imageData,
            Width = dstWidth,
            Height = dstHeight,
            PTS = ptsMs,  // Now in milliseconds
            Duration = _frame->duration,
            Stride = dstLinesize[0],
            BufferPool = _bufferPool // Track pool for returning buffer
        };

        return videoFrame;
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
        if (_swsContext != null)
        {
            ffmpeg.sws_freeContext(_swsContext);
            _swsContext = null;
        }

        if (_hwFrame != null)
        {
            var frame = _hwFrame;
            ffmpeg.av_frame_free(&frame);
            _hwFrame = null;
        }

        if (_frame != null)
        {
            var frame = _frame;
            ffmpeg.av_frame_free(&frame);
            _frame = null;
        }

        if (_hwDeviceCtx != null)
        {
            var ctx = _hwDeviceCtx;
            ffmpeg.av_buffer_unref(&ctx);
            _hwDeviceCtx = null;
        }

        if (_codecContext != null)
        {
            var ctx = _codecContext;
            ffmpeg.avcodec_free_context(&ctx);
            _codecContext = null;
        }

        _isOpen = false;
        _hwAccelEnabled = false;
    }

    public void Dispose()
    {
        Close();
    }

    public bool IsOpen => _isOpen;
    public int Width => _codecContext != null ? _codecContext->width : 0;
    public int Height => _codecContext != null ? _codecContext->height : 0;

    /// <summary>
    /// Check if pixel format is a hardware format that needs transfer to CPU
    /// </summary>
    private bool IsHardwarePixelFormat(AVPixelFormat format)
    {
        return format switch
        {
            AVPixelFormat.AV_PIX_FMT_VAAPI => true,          // Linux Intel/AMD
            AVPixelFormat.AV_PIX_FMT_VDPAU => true,          // Linux NVIDIA
            AVPixelFormat.AV_PIX_FMT_DXVA2_VLD => true,      // Windows 7/8 DirectX
            AVPixelFormat.AV_PIX_FMT_D3D11 => true,          // Windows 10+ Direct3D 11
            AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX => true,   // macOS VideoToolbox
            AVPixelFormat.AV_PIX_FMT_QSV => true,            // Intel Quick Sync
            AVPixelFormat.AV_PIX_FMT_CUDA => true,           // NVIDIA CUDA
            _ => false
        };
    }
}

/// <summary>
/// Decoded video frame data
/// </summary>
public class VideoFrame
{
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public int Width { get; set; }
    public int Height { get; set; }
    public long PTS { get; set; }
    public long Duration { get; set; }
    public int Stride { get; set; }
    internal Utils.BufferPool? BufferPool { get; set; } // For returning buffer to pool

    /// <summary>
    /// Return the frame buffer to the pool for reuse
    /// Call this when done rendering the frame
    /// </summary>
    public void ReturnBuffer()
    {
        if (BufferPool != null && Data != null)
        {
            BufferPool.Return(Data);
            Data = Array.Empty<byte>(); // Clear reference
        }
    }
}
