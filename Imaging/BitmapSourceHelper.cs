using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FrozenOCR.Imaging;

internal static class BitmapSourceHelper
{
    public static BitmapSource ToBitmapSource(Bitmap bitmap, double dpiX, double dpiY)
    {
        // We build a BitmapSource with the correct DPI so WPF doesn't "zoom" it on scaled monitors.
        // We also use a tight stride to avoid surprises with padding.
        using var bgra = Ensure32bppPArgb(bitmap);

        var rect = new Rectangle(0, 0, bgra.Width, bgra.Height);
        var data = bgra.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            var width = bgra.Width;
            var height = bgra.Height;
            var tightStride = width * 4;
            var srcStride = Math.Abs(data.Stride);

            var pixels = new byte[tightStride * height];
            for (var y = 0; y < height; y++)
            {
                var srcOffset = (data.Stride < 0)
                    ? (height - 1 - y) * srcStride
                    : y * srcStride;

                var srcRow = IntPtr.Add(data.Scan0, srcOffset);
                Marshal.Copy(srcRow, pixels, y * tightStride, tightStride);
            }

            var source = BitmapSource.Create(
                pixelWidth: width,
                pixelHeight: height,
                dpiX: dpiX,
                dpiY: dpiY,
                pixelFormat: PixelFormats.Pbgra32,
                palette: null,
                pixels: pixels,
                stride: tightStride
            );

            source.Freeze();
            return source;
        }
        finally
        {
            bgra.UnlockBits(data);
        }
    }

    private static Bitmap Ensure32bppPArgb(Bitmap bitmap)
    {
        if (bitmap.PixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppPArgb)
        {
            // Clone so caller can dispose original independently.
            return bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        }

        var converted = new Bitmap(bitmap.Width, bitmap.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(converted);
        g.DrawImage(bitmap, 0, 0, bitmap.Width, bitmap.Height);
        return converted;
    }
}

