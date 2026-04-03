using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace FrozenOCR.Ocr;

internal static class OcrTextFormatter
{
    public static string BuildCopyText(IReadOnlyList<OcrWordBox> words)
    {
        var temp = new List<OcrWord>(words.Count);
        for (var i = 0; i < words.Count; i++)
        {
            var w = words[i];
            temp.Add(new OcrWord(LineIndex: 0, WordIndex: i, Text: w.Text, X: w.X, Y: w.Y, Width: w.Width, Height: w.Height));
        }

        var normalized = LayoutNormalizer.Normalize(temp);
        var items = new List<(OcrWord Word, Rect Rect)>(normalized.Count);
        foreach (var w in normalized)
        {
            items.Add((w, new Rect(w.X, w.Y, w.Width, w.Height)));
        }

        return BuildCopyText(items);
    }

    public static string BuildCopyText(IReadOnlyList<(OcrWord Word, Rect Rect)> words)
    {
        if (words.Count == 0)
        {
            return string.Empty;
        }

        var byLine = new Dictionary<int, List<(OcrWord Word, Rect Rect)>>();
        foreach (var item in words)
        {
            if (!byLine.TryGetValue(item.Word.LineIndex, out var list))
            {
                list = new List<(OcrWord, Rect)>();
                byLine[item.Word.LineIndex] = list;
            }
            list.Add(item);
        }

        var lines = byLine.Keys.ToList();
        lines.Sort();

        var outputLines = new List<string>(lines.Count);
        string? pendingJoin = null;

        foreach (var lineIndex in lines)
        {
            var lineWords = byLine[lineIndex];
            lineWords.Sort((a, b) => a.Rect.X.CompareTo(b.Rect.X));

            var avgHeight = lineWords.Count == 0 ? 0 : lineWords.Average(w => w.Rect.Height);
            var avgWidth = lineWords.Count == 0 ? 0 : lineWords.Average(w => w.Rect.Width);
            var gapThreshold = Math.Max(1.0, Math.Max(avgHeight * 0.35, avgWidth * 0.18));

            var parts = new List<string>(lineWords.Count);
            Rect? prevRect = null;
            string? prevText = null;

            foreach (var (word, rect) in lineWords)
            {
                var text = NormalizePunctuation(word.Text);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (prevRect is not null)
                {
                    var gap = rect.X - prevRect.Value.Right;
                    var needSpace = gap > gapThreshold;

                    if (prevText is not null)
                    {
                        if (StartsWithNoSpace(text) || EndsWithNoSpace(prevText))
                        {
                            needSpace = false;
                        }
                    }

                    if (needSpace)
                    {
                        parts.Add(" ");
                    }
                }

                parts.Add(text);
                prevRect = rect;
                prevText = text;
            }

            var lineText = string.Concat(parts).Trim();
            if (string.IsNullOrWhiteSpace(lineText))
            {
                continue;
            }

            if (pendingJoin is not null)
            {
                lineText = pendingJoin + lineText;
                pendingJoin = null;
            }

            if (lineText.EndsWith("-", StringComparison.Ordinal) && lineText.Length > 1)
            {
                var trimmed = lineText.TrimEnd('-');
                pendingJoin = trimmed;
                continue;
            }

            outputLines.Add(lineText);
        }

        if (pendingJoin is not null)
        {
            outputLines.Add(pendingJoin);
        }

        return string.Join(Environment.NewLine, outputLines);
    }

    private static string NormalizePunctuation(string text)
    {
        return text
            .Replace('“', '"')
            .Replace('”', '"')
            .Replace('’', '\'')
            .Replace('‘', '\'')
            .Replace("`", "'");
    }

    private static bool StartsWithNoSpace(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return ",.:;!?)]}".Contains(text[0]);
    }

    private static bool EndsWithNoSpace(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return "([{".Contains(text[^1]);
    }
}
