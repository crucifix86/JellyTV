using FFmpeg.AutoGen;
using System.Runtime.InteropServices;

namespace AvaloniaMediaPlayer.Core.Codecs;

/// <summary>
/// Hardware acceleration configuration (inspired by Kodi's hardware decode support)
/// </summary>
public static class HardwareAcceleration
{
    public enum HWAccelType
    {
        None,
        VAAPI,      // Linux (Intel/AMD)
        DXVA2,      // Windows (DirectX Video Acceleration)
        D3D11VA,    // Windows 10+ (Direct3D 11)
        VideoToolbox, // macOS
        NVDEC,      // NVIDIA
        QSV,        // Intel Quick Sync
        VDPAU       // Linux (NVIDIA)
    }

    public static unsafe AVHWDeviceType GetHWDeviceType(HWAccelType accelType)
    {
        return accelType switch
        {
            HWAccelType.VAAPI => AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI,
            HWAccelType.DXVA2 => AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2,
            HWAccelType.D3D11VA => AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
            HWAccelType.VideoToolbox => AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX,
            HWAccelType.VDPAU => AVHWDeviceType.AV_HWDEVICE_TYPE_VDPAU,
            HWAccelType.QSV => AVHWDeviceType.AV_HWDEVICE_TYPE_QSV,
            _ => AVHWDeviceType.AV_HWDEVICE_TYPE_NONE
        };
    }

    public static HWAccelType DetectBestAcceleration()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Prefer D3D11VA on Windows 10+, fallback to DXVA2
            return Environment.OSVersion.Version.Major >= 10 ? HWAccelType.D3D11VA : HWAccelType.DXVA2;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return HWAccelType.VideoToolbox;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Try VAAPI first (Intel/AMD), fallback to VDPAU (NVIDIA)
            if (File.Exists("/dev/dri/renderD128") || File.Exists("/dev/dri/card0"))
            {
                return HWAccelType.VAAPI;
            }
            return HWAccelType.VDPAU;
        }

        return HWAccelType.None;
    }

    public static unsafe bool TryCreateHWContext(AVCodecContext* codecContext, HWAccelType accelType, out AVBufferRef* hwDeviceCtx)
    {
        hwDeviceCtx = null;

        if (accelType == HWAccelType.None)
            return false;

        var hwType = GetHWDeviceType(accelType);
        if (hwType == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
            return false;

        AVBufferRef* deviceCtx = null;
        var ret = ffmpeg.av_hwdevice_ctx_create(&deviceCtx, hwType, null, null, 0);

        if (ret < 0)
        {
            Console.WriteLine($"Failed to create HW device context for {accelType}: {ret}");
            return false;
        }

        codecContext->hw_device_ctx = ffmpeg.av_buffer_ref(deviceCtx);
        hwDeviceCtx = deviceCtx;

        Console.WriteLine($"Hardware acceleration enabled: {accelType}");
        return true;
    }

    public static unsafe AVPixelFormat GetHWPixelFormat(AVHWDeviceType type, AVPixelFormat* formats)
    {
        for (int i = 0; formats[i] != AVPixelFormat.AV_PIX_FMT_NONE; i++)
        {
            var hwConfig = ffmpeg.avcodec_get_hw_config(null, i);
            if (hwConfig != null && hwConfig->device_type == type)
            {
                return formats[i];
            }
        }
        return AVPixelFormat.AV_PIX_FMT_NONE;
    }
}
