using System;
using System.Collections.Generic;

namespace FrozenOCR.Ocr;

internal static class LayoutNormalizer
{
    public static IReadOnlyList<OcrWord> Normalize(IReadOnlyList<OcrWord> words)
    {
        if (words.Count == 0)
        {
            return Array.Empty<OcrWord>();
        }

        var items = new List<WordItem>(words.Count);
        foreach (var w in words)
        {
            var centerY = w.Y + (w.Height * 0.5);
            items.Add(new WordItem(w, centerY));
        }

        items.Sort((a, b) => a.CenterY.CompareTo(b.CenterY));

        var lines = new List<LineBucket>();
        foreach (var item in items)
        {
            LineBucket? best = null;
            var bestDelta = double.MaxValue;
            foreach (var line in lines)
            {
                var threshold = Math.Max(item.Word.Height, line.AvgHeight) * 0.6;
                var delta = Math.Abs(item.CenterY - line.CenterY);
                if (delta <= threshold && delta < bestDelta)
                {
                    bestDelta = delta;
                    best = line;
                }
            }

            if (best is null)
            {
                var bucket = new LineBucket(item.CenterY, item.Word.Height);
                bucket.Words.Add(item.Word);
                lines.Add(bucket);
            }
            else
            {
                best.Words.Add(item.Word);
                best.CenterY = (best.CenterY * best.Count + item.CenterY) / (best.Count + 1);
                best.AvgHeight = (best.AvgHeight * best.Count + item.Word.Height) / (best.Count + 1);
                best.Count++;
            }
        }

        lines.Sort((a, b) => a.CenterY.CompareTo(b.CenterY));

        var normalized = new List<OcrWord>(words.Count);
        for (var li = 0; li < lines.Count; li++)
        {
            var line = lines[li];
            line.Words.Sort((a, b) => a.X.CompareTo(b.X));
            for (var wi = 0; wi < line.Words.Count; wi++)
            {
                var w = line.Words[wi];
                normalized.Add(new OcrWord(
                    LineIndex: li,
                    WordIndex: wi,
                    Text: w.Text,
                    X: w.X,
                    Y: w.Y,
                    Width: w.Width,
                    Height: w.Height
                ));
            }
        }

        return normalized;
    }

    private sealed class LineBucket
    {
        public double CenterY;
        public double AvgHeight;
        public int Count;
        public readonly List<OcrWord> Words = new();

        public LineBucket(double centerY, double height)
        {
            CenterY = centerY;
            AvgHeight = height;
            Count = 1;
        }
    }

    private readonly record struct WordItem(OcrWord Word, double CenterY);
}
