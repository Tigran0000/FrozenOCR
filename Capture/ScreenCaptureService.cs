using System.Drawing;
using System.Drawing.Imaging;
using FrozenOCR.Display;

namespace FrozenOCR.Capture;

internal sealed class ScreenCaptureService
{
    public Bitmap CaptureMonitor(MonitorInfo monitor)
    {
        var bitmap = new Bitmap(monitor.PixelWidth, monitor.PixelHeight, PixelFormat.Format32bppPArgb);
        bitmap.SetResolution(monitor.DpiX, monitor.DpiY);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(
            sourceX: monitor.PixelLeft,
            sourceY: monitor.PixelTop,
            destinationX: 0,
            destinationY: 0,
            blockRegionSize: new Size(monitor.PixelWidth, monitor.PixelHeight),
            copyPixelOperation: CopyPixelOperation.SourceCopy
        );
        return bitmap;
    }
}

