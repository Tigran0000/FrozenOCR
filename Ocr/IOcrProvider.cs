using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;

namespace FrozenOCR.Ocr;

internal interface IOcrProvider
{
    string Id { get; }
    string DisplayName { get; }
    bool IsAvailable();
    IReadOnlyList<OcrLanguage> GetAvailableLanguages();
    Task<OcrResult> RecognizeAsync(Bitmap bitmap, OcrOptions options);
}

internal readonly record struct OcrLanguage(string Tag, string DisplayName, bool IsInstalled);
