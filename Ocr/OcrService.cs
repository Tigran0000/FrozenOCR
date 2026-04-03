using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using FrozenOCR.Core;
using FrozenOCR.ImageProcessing;
using FrozenOCR.Ocr.Providers;
using FrozenOCR.Settings;

namespace FrozenOCR.Ocr;

internal sealed class OcrService
{
    private readonly List<IOcrProvider> _providers;
    private readonly IImagePreprocessor _preprocessor;
    private readonly SettingsService _settingsService;

    public OcrService(SettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _providers = new List<IOcrProvider>
        {
            new WindowsAiOcrProvider(),
            new WindowsMediaOcrProvider()
        };
        _preprocessor = new ImagePreprocessor();
    }

    public IReadOnlyList<OcrLanguage> GetAvailableLanguages(OcrProviderMode mode)
    {
        var provider = ResolveProvider(mode, allowFallback: true);
        return provider?.GetAvailableLanguages() ?? Array.Empty<OcrLanguage>();
    }

    public OcrResult Recognize(Bitmap cropped, int padding = 0)
    {
        var ocrSettings = _settingsService.GetOcrSettings();
        var options = new OcrOptions
        {
            LanguageMode = ocrSettings.LanguageMode,
            LanguageTag = ocrSettings.LanguageTag,
            RecognitionMode = ocrSettings.RecognitionMode,
            PreprocessProfile = ocrSettings.PreprocessProfile,
            ReturnLayout = true
        };
        if (options.RecognitionMode == RecognitionMode.CodeTerminal && options.PreprocessProfile == PreprocessProfile.Auto)
        {
            options = options with { PreprocessProfile = PreprocessProfile.CodeHighContrast };
        }

        return RecognizeWithProfiles(cropped, options, padding);
    }

    private OcrResult RecognizeWithProfiles(Bitmap cropped, OcrOptions options, int padding)
    {
        var candidates = new List<(OcrResult Result, int Score, int Scale, int Padding)>();
        Log.Info($"OCR options profile={options.PreprocessProfile} mode={options.RecognitionMode} langMode={options.LanguageMode} tag={options.LanguageTag}");

        // Raw pass
        var raw = TryRecognize(cropped, options, scale: 1, padding: padding, tag: "raw");
        if (raw.HasValue) candidates.Add(raw.Value);

        // Profile-based passes
        if (options.PreprocessProfile == PreprocessProfile.CodeBinarized)
        {
            var bin = Binarize(cropped, scale: 3);
            using var binBitmap = bin.Bitmap;
            var res = TryRecognize(binBitmap, options, bin.Scale, padding: padding, tag: "bin");
            if (res.HasValue) candidates.Add(res.Value);
        }
        else if (options.PreprocessProfile == PreprocessProfile.CodeHighContrast)
        {
            var pass = _preprocessor.Process(cropped, PreprocessMode.Best, darkMode: false);
            using var processed = pass.Processed;
            var res = TryRecognize(processed, options, pass.Scale, padding: padding, tag: pass.Tag);
            if (res.HasValue) candidates.Add(res.Value);
        }
        else
        {
            var fast = _preprocessor.Process(cropped, PreprocessMode.Fast, darkMode: false);
            using var fastBmp = fast.Processed;
            var fastRes = TryRecognize(fastBmp, options, fast.Scale, padding: padding, tag: fast.Tag);
            if (fastRes.HasValue) candidates.Add(fastRes.Value);

            var best = _preprocessor.Process(cropped, PreprocessMode.Best, darkMode: false);
            using var bestBmp = best.Processed;
            var bestRes = TryRecognize(bestBmp, options, best.Scale, padding: padding, tag: best.Tag);
            if (bestRes.HasValue) candidates.Add(bestRes.Value);

            if (fast.AverageLuminance < 0.4)
            {
                var dark = _preprocessor.Process(cropped, PreprocessMode.Best, darkMode: true);
                using var darkBmp = dark.Processed;
                var darkRes = TryRecognize(darkBmp, options, dark.Scale, padding: padding, tag: dark.Tag);
                if (darkRes.HasValue) candidates.Add(darkRes.Value);
            }
        }

        if (candidates.Count == 0)
        {
            return new OcrResult { Text = string.Empty };
        }

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        var bestCandidate = candidates[0];
        Log.Info($"OCR selected pass score={bestCandidate.Score} provider={bestCandidate.Result.ProviderId} lang={bestCandidate.Result.LanguageTag} duration={bestCandidate.Result.Duration.TotalMilliseconds:F0}ms");
        return bestCandidate.Result;
    }

    private (OcrResult Result, int Score, int Scale, int Padding)? TryRecognize(
        Bitmap bitmap,
        OcrOptions options,
        int scale,
        int padding,
        string tag)
    {
        var primary = ResolveProvider(_settingsService.GetOcrSettings().ProviderMode, allowFallback: true);
        if (primary is null)
        {
            return null;
        }

        try
        {
            var sw = Stopwatch.StartNew();
            var result = primary.RecognizeAsync(bitmap, options).GetAwaiter().GetResult();
            result = MapResult(result, scale, padding);
            sw.Stop();
            var score = ScoreResult(result);
            return (result with { Duration = sw.Elapsed }, score, scale, padding);
        }
        catch (Exception ex)
        {
            Log.Error($"OCR provider failed tag={tag} provider={primary.Id}: {ex.Message}");
        }

        // Fallback to any other available provider.
        foreach (var provider in _providers)
        {
            if (provider == primary || !provider.IsAvailable())
            {
                continue;
            }
            try
            {
                var sw = Stopwatch.StartNew();
                var result = provider.RecognizeAsync(bitmap, options).GetAwaiter().GetResult();
                result = MapResult(result, scale, padding);
                sw.Stop();
                var score = ScoreResult(result);
                return (result with { Duration = sw.Elapsed }, score, scale, padding);
            }
            catch (Exception ex)
            {
                Log.Error($"OCR fallback failed tag={tag} provider={provider.Id}: {ex.Message}");
            }
        }

        return null;
    }

    private IOcrProvider? ResolveProvider(OcrProviderMode mode, bool allowFallback)
    {
        IOcrProvider? selected = mode switch
        {
            OcrProviderMode.WindowsAi => _providers.Find(p => p.Id == "windows-ai"),
            OcrProviderMode.WindowsMedia => _providers.Find(p => p.Id == "windows-media"),
            _ => _providers.Find(p => p.Id == "windows-ai") ?? _providers.Find(p => p.Id == "windows-media")
        };

        if (selected is not null && selected.IsAvailable())
        {
            Log.Info($"OCR provider selected={selected.Id}");
            return selected;
        }

        if (!allowFallback)
        {
            return null;
        }

        foreach (var provider in _providers)
        {
            if (provider.IsAvailable())
            {
                Log.Info($"OCR provider fallback={provider.Id}");
                return provider;
            }
        }
        return null;
    }

    private static int ScoreResult(OcrResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Text))
        {
            return 0;
        }

        var alnum = 0;
        var junk = 0;
        var spaces = 0;
        var latin = 0;
        var cyrillic = 0;
        var digits = 0;
        var letters = 0;
        var codeSymbols = 0;

        foreach (var ch in result.Text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                alnum++;
                if (char.IsLetter(ch))
                {
                    letters++;
                    if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z'))
                    {
                        latin++;
                    }
                    else if (ch >= '\u0400' && ch <= '\u04FF')
                    {
                        cyrillic++;
                    }
                }
                else
                {
                    digits++;
                }
            }
            else if (char.IsWhiteSpace(ch))
            {
                spaces++;
            }
            else
            {
                junk++;
                if ("{}[]()<>;=+-*/\\|_.".Contains(ch))
                {
                    codeSymbols++;
                }
            }
        }

        var score = alnum - (junk * 2) + (spaces / 2);
        if (letters > digits * 2)
        {
            score += 4;
        }
        if (cyrillic > 0 && latin > 0)
        {
            score -= 6;
        }
        if (codeSymbols > 0)
        {
            score += Math.Min(6, codeSymbols / 2);
        }
        return score;
    }

    private static (Bitmap Bitmap, int Scale) Binarize(Bitmap input, int scale)
    {
        var pre = new ImagePreprocessor();
        using var scaled = pre.Process(input, PreprocessMode.Best, darkMode: false).Processed;
        var gray = ToGrayscaleCopy(scaled);
        var threshold = ComputeThreshold(gray);
        var bin = ApplyThreshold(gray, threshold);
        gray.Dispose();
        return (bin, scale);
    }

    private static Bitmap ToGrayscaleCopy(Bitmap input)
    {
        var gray = new Bitmap(input.Width, input.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(gray);
        var matrix = new System.Drawing.Imaging.ColorMatrix(new[]
        {
            new[] { 0.299f, 0.299f, 0.299f, 0f, 0f },
            new[] { 0.587f, 0.587f, 0.587f, 0f, 0f },
            new[] { 0.114f, 0.114f, 0.114f, 0f, 0f },
            new[] { 0f,     0f,     0f,     1f, 0f },
            new[] { 0f,     0f,     0f,     0f, 1f }
        });
        using var attributes = new System.Drawing.Imaging.ImageAttributes();
        attributes.SetColorMatrix(matrix);
        g.DrawImage(input, new Rectangle(0, 0, gray.Width, gray.Height), 0, 0, input.Width, input.Height, GraphicsUnit.Pixel, attributes);
        return gray;
    }

    private static byte ComputeThreshold(Bitmap gray)
    {
        var rect = new Rectangle(0, 0, gray.Width, gray.Height);
        var data = gray.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, gray.PixelFormat);
        try
        {
            long sum = 0;
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * gray.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            for (var y = 0; y < gray.Height; y++)
            {
                var row = y * stride;
                for (var x = 0; x < gray.Width; x++)
                {
                    sum += bytes[row + (x * 3)];
                }
            }
            return (byte)(sum / (gray.Width * gray.Height));
        }
        finally
        {
            gray.UnlockBits(data);
        }
    }

    private static Bitmap ApplyThreshold(Bitmap gray, byte threshold)
    {
        var rect = new Rectangle(0, 0, gray.Width, gray.Height);
        var data = gray.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, gray.PixelFormat);
        var outBmp = new Bitmap(gray.Width, gray.Height, gray.PixelFormat);
        var outData = outBmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, gray.PixelFormat);
        try
        {
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * gray.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            for (var y = 0; y < gray.Height; y++)
            {
                var row = y * stride;
                for (var x = 0; x < gray.Width; x++)
                {
                    var idx = row + (x * 3);
                    var v = bytes[idx] >= threshold ? (byte)255 : (byte)0;
                    bytes[idx] = v;
                    bytes[idx + 1] = v;
                    bytes[idx + 2] = v;
                }
            }
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, outData.Scan0, bytes.Length);
        }
        finally
        {
            gray.UnlockBits(data);
            outBmp.UnlockBits(outData);
        }
        return outBmp;
    }

    private static OcrResult MapResult(OcrResult result, int scale, int padding)
    {
        if (result.Words is null || result.Words.Count == 0 || scale <= 1 && padding == 0)
        {
            return result;
        }

        var inv = 1.0 / Math.Max(1, scale);
        var mapped = new List<OcrWordBox>(result.Words.Count);
        foreach (var w in result.Words)
        {
            mapped.Add(new OcrWordBox(
                w.Text,
                (w.X * inv) - padding,
                (w.Y * inv) - padding,
                w.Width * inv,
                w.Height * inv
            ));
        }

        return result with { Words = mapped };
    }
}
