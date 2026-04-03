using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Windows.Media.Ocr;

namespace FrozenOCR.Ocr.Providers;

internal sealed class WindowsMediaOcrProvider : IOcrProvider
{
    private readonly WindowsOcrService _service = new();

    public string Id => "windows-media";
    public string DisplayName => "Windows Media OCR";

    public bool IsAvailable()
    {
        return OcrEngine.TryCreateFromUserProfileLanguages() is not null;
    }

    public IReadOnlyList<OcrLanguage> GetAvailableLanguages()
    {
        var list = new List<OcrLanguage>();
        try
        {
            foreach (var lang in OcrEngine.AvailableRecognizerLanguages)
            {
                var name = string.IsNullOrWhiteSpace(lang.NativeName)
                    ? lang.LanguageTag
                    : $"{lang.NativeName} ({lang.LanguageTag})";
                list.Add(new OcrLanguage(lang.LanguageTag, name, true));
            }
        }
        catch
        {
            // Ignore if not available
        }
        return list;
    }

    public async Task<OcrResult> RecognizeAsync(Bitmap bitmap, OcrOptions options)
    {
        var languageTag = options.LanguageMode == LanguageMode.Specific
            ? options.LanguageTag
            : Settings.OcrSettings.Auto;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var layout = await _service.RecognizeLayoutAsync(bitmap, languageTag);
        sw.Stop();

        var words = new List<OcrWordBox>(layout.Words.Count);
        foreach (var w in layout.Words)
        {
            words.Add(new OcrWordBox(w.Text, w.X, w.Y, w.Width, w.Height));
        }

        return new OcrResult
        {
            Text = OcrTextFormatter.BuildCopyText(words),
            Words = options.ReturnLayout ? words : null,
            ProviderId = Id,
            ProviderName = DisplayName,
            LanguageTag = languageTag,
            Duration = sw.Elapsed
        };
    }
}
