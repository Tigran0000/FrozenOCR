using System;
using System.Drawing;
using System.Windows;

namespace FrozenOCR.ImageProcessing;

internal sealed class BitmapCropService
{
    public Bitmap Crop(Bitmap source, Int32Rect pixelRect)
    {
        if (pixelRect.Width <= 0 || pixelRect.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelRect), "Selection rectangle is empty.");
        }

        var r = new Rectangle(pixelRect.X, pixelRect.Y, pixelRect.Width, pixelRect.Height);
        var bounds = new Rectangle(0, 0, source.Width, source.Height);
        r.Intersect(bounds);

        if (r.Width <= 0 || r.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelRect), "Selection rectangle is outside image bounds.");
        }

        return source.Clone(r, source.PixelFormat);
    }

    public Bitmap CropWithPadding(Bitmap source, Int32Rect pixelRect, int padding, out Int32Rect paddedRect)
    {
        var x = pixelRect.X - padding;
        var y = pixelRect.Y - padding;
        var w = pixelRect.Width + (padding * 2);
        var h = pixelRect.Height + (padding * 2);

        if (w <= 0 || h <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelRect), "Selection rectangle is empty.");
        }

        var bounds = new Rectangle(0, 0, source.Width, source.Height);
        var r = new Rectangle(x, y, w, h);
        r.Intersect(bounds);

        if (r.Width <= 0 || r.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelRect), "Selection rectangle is outside image bounds.");
        }

        paddedRect = new Int32Rect(r.X, r.Y, r.Width, r.Height);
        return source.Clone(r, source.PixelFormat);
    }
}

