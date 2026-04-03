using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using FrozenOCR.Imaging;

namespace FrozenOCR.Core;

internal sealed class ScreenshotExportService
{
    public Task<ScreenshotSaveResult?> SaveSelectionAsync(
        Bitmap screenshot,
        Int32Rect selectionPx,
        string saveFolder,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rect = new Rectangle(selectionPx.X, selectionPx.Y, selectionPx.Width, selectionPx.Height);
            var bounded = Rectangle.Intersect(new Rectangle(0, 0, screenshot.Width, screenshot.Height), rect);
            if (bounded.Width <= 0 || bounded.Height <= 0)
            {
                return (ScreenshotSaveResult?)null;
            }

            var folder = string.IsNullOrWhiteSpace(saveFolder)
                ? GetDefaultFolder()
                : saveFolder.Trim();
            Directory.CreateDirectory(folder);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var baseName = $"FrozenOCR_{timestamp}";
            var path = Path.Combine(folder, $"{baseName}.png");
            var index = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(folder, $"{baseName}_{index}.png");
                index++;
            }

            using var cropped = screenshot.Clone(bounded, PixelFormat.Format32bppPArgb);
            cropped.Save(path, ImageFormat.Png);
            var source = BitmapSourceHelper.ToBitmapSource(cropped, screenshot.HorizontalResolution, screenshot.VerticalResolution);
            return new ScreenshotSaveResult(path, source);
        }, cancellationToken);
    }

    public static string GetDefaultFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Screenshots");
    }
}

internal sealed record ScreenshotSaveResult(string Path, BitmapSource Image);
