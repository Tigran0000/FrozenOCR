using System.Collections.Generic;

namespace FrozenOCR.Ocr;

internal sealed class OcrLayout
{
    public required IReadOnlyList<OcrWord> Words { get; init; }
}

