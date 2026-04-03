using System.Collections.Generic;

namespace FrozenOCR.Ocr;

internal enum OcrProviderMode
{
    Auto,
    WindowsAi,
    WindowsMedia
}

internal enum LanguageMode
{
    Auto,
    Specific
}

internal enum RecognitionMode
{
    Default,
    CodeTerminal
}

internal enum PreprocessProfile
{
    Auto,
    CodeHighContrast,
    CodeBinarized,
    Document
}

internal sealed record OcrOptions
{
    public LanguageMode LanguageMode { get; init; } = LanguageMode.Auto;
    public string LanguageTag { get; init; } = "auto";
    public RecognitionMode RecognitionMode { get; init; } = RecognitionMode.Default;
    public bool ReturnLayout { get; init; } = true;
    public PreprocessProfile PreprocessProfile { get; init; } = PreprocessProfile.Auto;
}

internal sealed record OcrResult
{
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<OcrLine>? Lines { get; init; }
    public IReadOnlyList<OcrWordBox>? Words { get; init; }
    public string ProviderId { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public string LanguageTag { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public double? Confidence { get; init; }
}

internal readonly record struct OcrLine(int LineIndex, IReadOnlyList<OcrWordBox> Words);
internal readonly record struct OcrWordBox(string Text, double X, double Y, double Width, double Height);
