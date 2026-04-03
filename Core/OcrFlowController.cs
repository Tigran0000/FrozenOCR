using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;
using FrozenOCR.Capture;
using FrozenOCR.Display;
using FrozenOCR.Imaging;
using FrozenOCR.ImageProcessing;
using FrozenOCR.Ocr;
using FrozenOCR.Overlay;
using FrozenOCR.Settings;

namespace FrozenOCR.Core;

internal sealed class OcrFlowController
{
    private int _isRunning;
    private readonly MonitorService _monitorService;
    private readonly ScreenCaptureService _screenCaptureService;
    private readonly BitmapCropService _cropService;
    private readonly OcrService _ocrService;
    private readonly ScreenshotExportService _screenshotExportService;
    private readonly SettingsService _settingsService;

    private const int OcrPaddingPx = 2;

    public bool IsRunning => Interlocked.CompareExchange(ref _isRunning, 0, 0) == 1;

    public OcrFlowController(
        MonitorService monitorService,
        ScreenCaptureService screenCaptureService,
        SettingsService settingsService)
    {
        _monitorService = monitorService;
        _screenCaptureService = screenCaptureService;
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _cropService = new BitmapCropService();
        _ocrService = new OcrService(_settingsService);
        _screenshotExportService = new ScreenshotExportService();
    }

    public async Task TryStartAsync()
    {
        await TryStartForMonitorAsync(_monitorService.GetMonitorUnderCursor());
    }

    public async Task TryStartForPointAsync(System.Windows.Point screenPoint)
    {
        var pt = new Native.NativeMethods.POINT
        {
            X = (int)Math.Round(screenPoint.X),
            Y = (int)Math.Round(screenPoint.Y)
        };
        await TryStartForMonitorAsync(_monitorService.GetMonitorForPoint(pt));
    }

    private async Task TryStartForMonitorAsync(MonitorInfo monitor)
    {
        if (Interlocked.Exchange(ref _isRunning, 1) == 1)
        {
            return;
        }

        try
        {
            Log.Info($"Capture requested bounds={monitor.PixelLeft},{monitor.PixelTop} {monitor.PixelWidth}x{monitor.PixelHeight} dpi={monitor.DpiX}x{monitor.DpiY}");
            try
            {
                Bitmap screenshot = await Task.Run(() => _screenCaptureService.CaptureMonitor(monitor));
                try
                {
                    Log.Info($"Monitor capture complete size={screenshot.Width}x{screenshot.Height}");
                    var screenshotSource = BitmapSourceHelper.ToBitmapSource(screenshot, monitor.DpiX, monitor.DpiY);

                    var overlayClosed = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

                    FrozenOverlayWindow? overlay = null;
                    Action<System.Windows.Int32Rect, System.Windows.Rect, int>? confirmHandler = null;
                    Action<System.Windows.Int32Rect, Bitmap>? saveHandler = null;
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        overlay = new FrozenOverlayWindow(screenshotSource, screenshot, monitor, _settingsService);
                        overlay.Closed += (_, _) => overlayClosed.TrySetResult(null);

                        confirmHandler = (pixelRect, dipRect, version) =>
                        {
                            Log.Info($"OCR selection confirmed px={pixelRect.X},{pixelRect.Y} {pixelRect.Width}x{pixelRect.Height} version={version}");
                            // OCR only inside user selection.
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    Bitmap cropped;
                                    try
                                    {
                                        cropped = _cropService.CropWithPadding(screenshot, pixelRect, padding: OcrPaddingPx, out _);
                                    }
                                    catch (Exception ex)
                                    {
                                        await Application.Current.Dispatcher.InvokeAsync(() =>
                                        {
                                            MessageBox.Show(
                                                $"Crop failed:\r\n{ex.Message}",
                                                "FrozenOCR",
                                                MessageBoxButton.OK,
                                                MessageBoxImage.Error
                                            );
                                        });
                                        return;
                                    }

                                    try
                                    {
                                        Log.Info($"OCR crop size px={cropped.Width}x{cropped.Height}");
                                        var layout = RecognizeWithProvider(cropped);
                                        await Application.Current.Dispatcher.InvokeAsync(() =>
                                        {
                                            if (overlay is { IsVisible: true })
                                            {
                                                overlay.ApplyOcrLayout(layout, version);
                                            }
                                        });
                                    }
                                    finally
                                    {
                                        cropped.Dispose();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    await Application.Current.Dispatcher.InvokeAsync(() =>
                                    {
                                        if (overlay is { IsVisible: true })
                                        {
                                            MessageBox.Show(
                                                $"OCR failed:\r\n{ex.Message}",
                                                "FrozenOCR",
                                                MessageBoxButton.OK,
                                                MessageBoxImage.Error
                                            );
                                        }
                                    });
                                }
                            });
                        };

                        saveHandler = async (rect, fullScreenshot) =>
                        {
                            if (overlay is { IsVisible: true })
                            {
                                try
                                {
                                    var settings = _settingsService.GetScreenshotSettings();
                                    var result = await _screenshotExportService.SaveSelectionAsync(
                                        fullScreenshot,
                                        rect,
                                        settings.SaveFolder,
                                        CancellationToken.None);
                                    if (result is null || string.IsNullOrWhiteSpace(result.Path))
                                    {
                                        overlay.ShowToast("Invalid selection");
                                        return;
                                    }

                                    try
                                    {
                                        Clipboard.SetImage(result.Image);
                                    }
                                    catch
                                    {
                                        // Clipboard can be locked; ignore.
                                    }

                                    var fileName = Path.GetFileName(result.Path);
                                    var folder = Path.GetDirectoryName(result.Path) ?? string.Empty;
                                    Log.Info($"Screenshot saved path=\"{result.Path}\"");
                                    overlay.ShowToast($"Saved: {fileName}", "Open folder", () =>
                                    {
                                        if (!string.IsNullOrWhiteSpace(folder))
                                        {
                                            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"")
                                            {
                                                UseShellExecute = true
                                            });
                                        }
                                        overlay.CloseSafely();
                                    });
                                }
                                catch (Exception ex)
                                {
                                    overlay.ShowToast($"Save failed: {ex.Message}");
                                }
                            }
                        };
                        overlay.ConfirmOcrRequested += confirmHandler;
                        overlay.SaveScreenshotRequested += saveHandler;
                        overlay.WindowState = System.Windows.WindowState.Normal;
                        overlay.Show();
                        overlay.Activate();
                        Log.Info("Overlay opened");
                    });

                    await overlayClosed.Task;
                    Log.Info("Overlay closed");
                    Log.Memory("Overlay closed");

                    // Detach handler to avoid keeping references alive.
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (overlay is not null)
                        {
                            if (confirmHandler is not null) overlay.ConfirmOcrRequested -= confirmHandler;
                            if (saveHandler is not null) overlay.SaveScreenshotRequested -= saveHandler;
                        }
                    });
                }
                finally
                {
                    screenshot.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"OcrFlowController failed: {ex}");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show(
                        $"FrozenOCR error:\r\n{ex}",
                        "FrozenOCR",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                });
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, 0);
        }
    }

    private OcrLayout RecognizeWithProvider(Bitmap cropped)
    {
        var result = _ocrService.Recognize(cropped, OcrPaddingPx);
        if (result.Words is null || result.Words.Count == 0)
        {
            return new OcrLayout { Words = Array.Empty<OcrWord>() };
        }

        var words = new List<OcrWord>(result.Words.Count);
        for (var i = 0; i < result.Words.Count; i++)
        {
            var w = result.Words[i];
            words.Add(new OcrWord(
                LineIndex: 0,
                WordIndex: i,
                Text: w.Text,
                X: w.X,
                Y: w.Y,
                Width: w.Width,
                Height: w.Height
            ));
        }

        var normalized = LayoutNormalizer.Normalize(words);
        return new OcrLayout { Words = normalized };
    }
}
