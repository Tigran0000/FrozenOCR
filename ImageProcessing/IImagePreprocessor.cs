using System.Drawing;

namespace FrozenOCR.ImageProcessing;

internal enum PreprocessMode
{
    Fast,
    Best
}

internal readonly record struct ImagePreprocessResult(Bitmap Processed, double AverageLuminance, string Tag, int Scale);

internal interface IImagePreprocessor
{
    ImagePreprocessResult Process(Bitmap input, PreprocessMode mode, bool darkMode);
}
