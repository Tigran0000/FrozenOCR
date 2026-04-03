using System;

namespace FrozenOCR.Display;

internal readonly record struct MonitorInfo(
    IntPtr Handle,
    int PixelLeft,
    int PixelTop,
    int PixelWidth,
    int PixelHeight,
    uint DpiX,
    uint DpiY
)
{
    public double ScaleX => DpiX / 96.0;
    public double ScaleY => DpiY / 96.0;
}

