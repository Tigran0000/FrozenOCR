using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Graphics.Imaging;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Imaging;
using FrozenOCR.Settings;
using Windows.Graphics.Imaging;

namespace FrozenOCR.Ocr.Providers;

// Windows AI Text Recognition (primary when available).
internal sealed class WindowsAiOcrProvider : IOcrProvider
{
    private static readonly object Sync = new();
    private static Task<TextRecognizer?>? _recognizerTask;

    public string Id => "windows-ai";
    public string DisplayName => "Windows AI OCR";

    public bool IsAvailable()
    {
        try
        {
            if (Environment.OSVersion.Version.Build < 22000)
            {
                return false;
            }
            return TextRecognizer.GetReadyState() == AIFeatureReadyState.Ready;
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyList<OcrLanguage> GetAvailableLanguages()
    {
        // AI OCR language list is not currently exposed; return empty.
        return Array.Empty<OcrLanguage>();
    }

    public async Task<OcrResult> RecognizeAsync(Bitmap bitmap, OcrOptions options)
    {
        var recognizer = await GetRecognizerAsync();
        if (recognizer is null)
        {
            throw new InvalidOperationException("Windows AI OCR is not ready.");
        }

        using var software = ConvertToSoftwareBitmap(bitmap);
        var buffer = ImageBuffer.CreateForSoftwareBitmap(software);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var recognized = recognizer.RecognizeTextFromImage(buffer);
        sw.Stop();

        var lines = recognized.Lines;
        var words = new List<OcrWordBox>();
        var textBuilder = new StringBuilder();
        foreach (var line in lines)
        {
            textBuilder.AppendLine(line.Text);
            foreach (var word in line.Words)
            {
                var box = word.BoundingBox;
                var minX = Math.Min(Math.Min(box.TopLeft.X, box.TopRight.X), Math.Min(box.BottomLeft.X, box.BottomRight.X));
                var maxX = Math.Max(Math.Max(box.TopLeft.X, box.TopRight.X), Math.Max(box.BottomLeft.X, box.BottomRight.X));
                var minY = Math.Min(Math.Min(box.TopLeft.Y, box.TopRight.Y), Math.Min(box.BottomLeft.Y, box.BottomRight.Y));
                var maxY = Math.Max(Math.Max(box.TopLeft.Y, box.TopRight.Y), Math.Max(box.BottomLeft.Y, box.BottomRight.Y));
                words.Add(new OcrWordBox(word.Text, minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY)));
            }
        }

        return new OcrResult
        {
            Text = textBuilder.ToString().TrimEnd(),
            Words = options.ReturnLayout ? words : null,
            ProviderId = Id,
            ProviderName = DisplayName,
            LanguageTag = options.LanguageMode == LanguageMode.Specific ? options.LanguageTag : Settings.OcrSettings.Auto,
            Duration = sw.Elapsed
        };
    }

    private static Task<TextRecognizer?> GetRecognizerAsync()
    {
        lock (Sync)
        {
            _recognizerTask ??= CreateRecognizerAsync();
            return _recognizerTask;
        }
    }

    private static async Task<TextRecognizer?> CreateRecognizerAsync()
    {
        if (TextRecognizer.GetReadyState() != AIFeatureReadyState.Ready)
        {
            return null;
        }
        return await TextRecognizer.CreateAsync();
    }

    private static SoftwareBitmap ConvertToSoftwareBitmap(Bitmap bitmap)
    {
        using var bgra = Ensure32bppPArgb(bitmap);

        var rect = new Rectangle(0, 0, bgra.Width, bgra.Height);
        var data = bgra.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var width = bgra.Width;
            var height = bgra.Height;
            var stride = data.Stride;
            var srcStride = Math.Abs(stride);
            var tight = new byte[width * height * 4];

            for (var y = 0; y < height; y++)
            {
                var srcOffset = (stride < 0) ? (height - 1 - y) * srcStride : y * srcStride;
                var srcRow = IntPtr.Add(data.Scan0, srcOffset);
                Marshal.Copy(srcRow, tight, y * width * 4, width * 4);
            }

            var sb = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
            sb.CopyFromBuffer(tight.AsBuffer());
            return sb;
        }
        finally
        {
            bgra.UnlockBits(data);
        }
    }

    private static Bitmap Ensure32bppPArgb(Bitmap bitmap)
    {
        if (bitmap.PixelFormat == PixelFormat.Format32bppPArgb)
        {
            return bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height), PixelFormat.Format32bppPArgb);
        }

        var converted = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(converted);
        g.DrawImage(bitmap, 0, 0, bitmap.Width, bitmap.Height);
        return converted;
    }
}
