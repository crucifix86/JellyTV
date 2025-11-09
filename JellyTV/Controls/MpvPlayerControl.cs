using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using System;

namespace JellyTV.Controls;

public class MpvPlayerControl : NativeControlHost
{
    public IntPtr NativeHandle { get; private set; }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = base.CreateNativeControlCore(parent);
        NativeHandle = handle.Handle;
        Console.WriteLine($"MPV native control created with handle: {NativeHandle}");
        return handle;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        Console.WriteLine($"MPV native control destroyed");
        base.DestroyNativeControlCore(control);
    }
}
