using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using FrozenOCR.Settings;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace FrozenOCR.Ocr;

internal sealed class WindowsOcrService
{
    private static readonly Lazy<OcrEngine?> Engine = new(() => OcrEngine.TryCreateFromUserProfileLanguages());
    private static readonly Dictionary<string, OcrEngine?> EngineByTag = new(StringComparer.OrdinalIgnoreCase);

    public async Task<string> RecognizeAsync(Bitmap bitmap)
    {
        var layout = await RecognizeLayoutAsync(bitmap);
        // Keep current behavior: return plain text for callers that only need text.
        // We rebuild it using OCR line/word order (line-by-line).
        return string.Join(
            Environment.NewLine,
            BuildLines(layout.Words)
        );
    }

    public async Task<OcrLayout> RecognizeLayoutAsync(Bitmap bitmap, string? languageTag = null)
    {
        var engine = ResolveEngine(languageTag);
        if (engine is null)
        {
            throw new InvalidOperationException("Windows OCR engine is not available on this system.");
        }

        using var softwareBitmap = ConvertToSoftwareBitmapBgra8(bitmap);
        var result = await engine.RecognizeAsync(softwareBitmap);

        var words = new List<OcrWord>(capacity: 256);
        var lines = result.Lines;
        for (var li = 0; li < lines.Count; li++)
        {
            var line = lines[li];
            var lineWords = line.Words;
            for (var wi = 0; wi < lineWords.Count; wi++)
            {
                var w = lineWords[wi];
                var r = w.BoundingRect; // Windows.Foundation.Rect (image pixel coordinates)
                words.Add(new OcrWord(
                    LineIndex: li,
                    WordIndex: wi,
                    Text: w.Text ?? string.Empty,
                    X: r.X,
                    Y: r.Y,
                    Width: r.Width,
                    Height: r.Height
                ));
            }
        }

        return new OcrLayout { Words = words };
    }

    private static OcrEngine? ResolveEngine(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag) || languageTag == Settings.OcrSettings.Auto)
        {
            return Engine.Value;
        }

        if (EngineByTag.TryGetValue(languageTag, out var cached))
        {
            return cached ?? Engine.Value;
        }

        try
        {
            var lang = new Language(languageTag);
            var engine = OcrEngine.TryCreateFromLanguage(lang);
            EngineByTag[languageTag] = engine;
            return engine ?? Engine.Value;
        }
        catch
        {
            EngineByTag[languageTag] = null;
            return Engine.Value;
        }
    }

    private static SoftwareBitmap ConvertToSoftwareBitmapBgra8(Bitmap bitmap)
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

            // Tight BGRA buffer (width*height*4). WPF/system drawing often has stride padding.
            var tight = new byte[width * height * 4];

            for (var y = 0; y < height; y++)
            {
                var srcOffset = (stride < 0)
                    ? (height - 1 - y) * srcStride
                    : y * srcStride;

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
            // Clone so caller can dispose original independently.
            return bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height), PixelFormat.Format32bppPArgb);
        }

        var converted = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(converted);
        g.DrawImage(bitmap, 0, 0, bitmap.Width, bitmap.Height);
        return converted;
    }

    private static IEnumerable<string> BuildLines(IReadOnlyList<OcrWord> words)
    {
        var currentLine = -1;
        var line = new List<string>();

        for (var i = 0; i < words.Count; i++)
        {
            var w = words[i];
            if (w.LineIndex != currentLine)
            {
                if (line.Count > 0)
                {
                    yield return string.Join(' ', line);
                    line.Clear();
                }
                currentLine = w.LineIndex;
            }

            if (!string.IsNullOrWhiteSpace(w.Text))
            {
                line.Add(w.Text);
            }
        }

        if (line.Count > 0)
        {
            yield return string.Join(' ', line);
        }
    }
}

