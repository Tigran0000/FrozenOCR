using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FrozenOCR.ImageProcessing;

internal sealed class ImagePreprocessor : IImagePreprocessor
{
    public ImagePreprocessResult Process(Bitmap input, PreprocessMode mode, bool darkMode)
    {
        var scale = mode == PreprocessMode.Best ? 3 : 2;
        var sharpenAmount = mode == PreprocessMode.Best ? 0.9f : 0.6f;
        var tag = mode == PreprocessMode.Best ? "best" : "fast";
        if (darkMode)
        {
            tag += "_dark";
        }

        using var scaled = Scale(input, scale);
        using var gray = ToGrayscale(scaled);
        var avgLum = ComputeAverageLuminance(gray);
        using var contrast = StretchContrast(gray, darkMode ? 1.15f : 1.0f);
        var sharpened = UnsharpMask(contrast, amount: sharpenAmount);

        if (darkMode && avgLum < 0.35)
        {
            var inverted = Invert(sharpened);
            sharpened.Dispose();
        return new ImagePreprocessResult(inverted, avgLum, tag, scale);
        }

        return new ImagePreprocessResult(sharpened, avgLum, tag, scale);
    }

    private static Bitmap Scale(Bitmap input, int scale)
    {
        var target = new Bitmap(input.Width * scale, input.Height * scale, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(target);
        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        g.DrawImage(input, new Rectangle(0, 0, target.Width, target.Height));
        return target;
    }

    private static Bitmap ToGrayscale(Bitmap input)
    {
        var gray = new Bitmap(input.Width, input.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(gray);
        var matrix = new ColorMatrix(new[]
        {
            new[] { 0.299f, 0.299f, 0.299f, 0f, 0f },
            new[] { 0.587f, 0.587f, 0.587f, 0f, 0f },
            new[] { 0.114f, 0.114f, 0.114f, 0f, 0f },
            new[] { 0f,     0f,     0f,     1f, 0f },
            new[] { 0f,     0f,     0f,     0f, 1f }
        });
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(matrix);
        g.DrawImage(input, new Rectangle(0, 0, gray.Width, gray.Height), 0, 0, input.Width, input.Height, GraphicsUnit.Pixel, attributes);
        return gray;
    }

    private static double ComputeAverageLuminance(Bitmap gray)
    {
        var buffer = ReadBuffer(gray);
        long sum = 0;
        for (var y = 0; y < buffer.Height; y++)
        {
            var rowOffset = y * buffer.Stride;
            for (var x = 0; x < buffer.Width; x++)
            {
                sum += buffer.Bytes[rowOffset + (x * buffer.BytesPerPixel)];
            }
        }
        return sum / (double)(buffer.Width * buffer.Height * 255.0);
    }

    private static Bitmap StretchContrast(Bitmap input, float boost)
    {
        var buffer = ReadBuffer(input);
        byte min = 255;
        byte max = 0;
        for (var y = 0; y < buffer.Height; y++)
        {
            var rowOffset = y * buffer.Stride;
            for (var x = 0; x < buffer.Width; x++)
            {
                var v = buffer.Bytes[rowOffset + (x * buffer.BytesPerPixel)];
                if (v < min) min = v;
                if (v > max) max = v;
            }
        }

        if (max <= min + 2)
        {
            return (Bitmap)input.Clone();
        }

        var scale = 255f / (max - min);
        var outBytes = new byte[buffer.Bytes.Length];
        for (var y = 0; y < buffer.Height; y++)
        {
            var rowOffset = y * buffer.Stride;
            for (var x = 0; x < buffer.Width; x++)
            {
                var idx = rowOffset + (x * buffer.BytesPerPixel);
                var v = buffer.Bytes[idx];
                var stretched = (int)((v - min) * scale * boost);
                if (stretched < 0) stretched = 0;
                if (stretched > 255) stretched = 255;
                var s = (byte)stretched;
                outBytes[idx] = s;
                if (buffer.BytesPerPixel >= 3)
                {
                    outBytes[idx + 1] = s;
                    outBytes[idx + 2] = s;
                }
                if (buffer.BytesPerPixel == 4)
                {
                    outBytes[idx + 3] = buffer.Bytes[idx + 3];
                }
            }
        }

        return WriteBuffer(buffer with { Bytes = outBytes });
    }

    private static Bitmap UnsharpMask(Bitmap input, float amount)
    {
        var src = ReadBuffer(input);
        var blur = BoxBlur3x3Buffer(src);
        var outBytes = new byte[src.Bytes.Length];
        for (var y = 0; y < src.Height; y++)
        {
            var rowOffset = y * src.Stride;
            for (var x = 0; x < src.Width; x++)
            {
                var idx = rowOffset + (x * src.BytesPerPixel);
                var orig = src.Bytes[idx];
                var bl = blur[idx];
                var val = orig + (orig - bl) * amount;
                if (val < 0) val = 0;
                if (val > 255) val = 255;
                var v = (byte)val;
                outBytes[idx] = v;
                if (src.BytesPerPixel >= 3)
                {
                    outBytes[idx + 1] = v;
                    outBytes[idx + 2] = v;
                }
                if (src.BytesPerPixel == 4)
                {
                    outBytes[idx + 3] = src.Bytes[idx + 3];
                }
            }
        }
        return WriteBuffer(src with { Bytes = outBytes });
    }

    private static byte[] BoxBlur3x3Buffer(BitmapBuffer src)
    {
        var outBytes = new byte[src.Bytes.Length];
        for (var y = 0; y < src.Height; y++)
        {
            var rowOffset = y * src.Stride;
            for (var x = 0; x < src.Width; x++)
            {
                int sum = 0;
                int count = 0;
                for (var oy = -1; oy <= 1; oy++)
                {
                    var yy = y + oy;
                    if (yy < 0 || yy >= src.Height) continue;
                    var srcRow = yy * src.Stride;
                    for (var ox = -1; ox <= 1; ox++)
                    {
                        var xx = x + ox;
                        if (xx < 0 || xx >= src.Width) continue;
                        sum += src.Bytes[srcRow + (xx * src.BytesPerPixel)];
                        count++;
                    }
                }
                var v = (byte)(sum / Math.Max(1, count));
                var idx = rowOffset + (x * src.BytesPerPixel);
                outBytes[idx] = v;
                if (src.BytesPerPixel >= 3)
                {
                    outBytes[idx + 1] = v;
                    outBytes[idx + 2] = v;
                }
                if (src.BytesPerPixel == 4)
                {
                    outBytes[idx + 3] = src.Bytes[idx + 3];
                }
            }
        }
        return outBytes;
    }

    private static Bitmap Invert(Bitmap input)
    {
        var src = ReadBuffer(input);
        var outBytes = new byte[src.Bytes.Length];
        for (var y = 0; y < src.Height; y++)
        {
            var rowOffset = y * src.Stride;
            for (var x = 0; x < src.Width; x++)
            {
                var idx = rowOffset + (x * src.BytesPerPixel);
                var v = (byte)(255 - src.Bytes[idx]);
                outBytes[idx] = v;
                if (src.BytesPerPixel >= 3)
                {
                    outBytes[idx + 1] = v;
                    outBytes[idx + 2] = v;
                }
                if (src.BytesPerPixel == 4)
                {
                    outBytes[idx + 3] = src.Bytes[idx + 3];
                }
            }
        }
        return WriteBuffer(src with { Bytes = outBytes });
    }

    private static BitmapBuffer ReadBuffer(Bitmap input)
    {
        var rect = new Rectangle(0, 0, input.Width, input.Height);
        var data = input.LockBits(rect, ImageLockMode.ReadOnly, input.PixelFormat);
        try
        {
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * input.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return new BitmapBuffer(bytes, stride, input.Width, input.Height, Image.GetPixelFormatSize(input.PixelFormat) / 8, input.PixelFormat);
        }
        finally
        {
            input.UnlockBits(data);
        }
    }

    private static Bitmap WriteBuffer(BitmapBuffer buffer)
    {
        var bmp = new Bitmap(buffer.Width, buffer.Height, buffer.Format);
        var rect = new Rectangle(0, 0, buffer.Width, buffer.Height);
        var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, buffer.Format);
        try
        {
            var dstStride = Math.Abs(data.Stride);
            if (dstStride == buffer.Stride)
            {
                Marshal.Copy(buffer.Bytes, 0, data.Scan0, buffer.Bytes.Length);
            }
            else
            {
                for (var y = 0; y < buffer.Height; y++)
                {
                    var srcOffset = y * buffer.Stride;
                    var rowBytes = buffer.Width * buffer.BytesPerPixel;
                    var rowPtr = IntPtr.Add(data.Scan0, y * dstStride);
                    Marshal.Copy(buffer.Bytes, srcOffset, rowPtr, rowBytes);
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }

    private readonly record struct BitmapBuffer(
        byte[] Bytes,
        int Stride,
        int Width,
        int Height,
        int BytesPerPixel,
        PixelFormat Format);
}
