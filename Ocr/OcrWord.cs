namespace FrozenOCR.Ocr;

internal readonly record struct OcrWord(
    int LineIndex,
    int WordIndex,
    string Text,
    double X,
    double Y,
    double Width,
    double Height
);

