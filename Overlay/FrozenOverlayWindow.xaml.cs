using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using Cursors = System.Windows.Input.Cursors;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseEventHandler = System.Windows.Input.MouseEventHandler;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using FrozenOCR.Core;
using FrozenOCR.Display;
using FrozenOCR.Input;
using FrozenOCR.Ocr;
using FrozenOCR.Ocr.Providers;
using OcrWord = FrozenOCR.Ocr.OcrWord;
using FrozenOCR.Settings;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace FrozenOCR.Overlay;

internal partial class FrozenOverlayWindow : Window
{
    private readonly record struct WordHit(int WordIndex, int LineIndex);
    #if DEBUG
    private const bool ShowBuildMarker = true;  // set false to hide red banner when debugging
#else
    private const bool ShowBuildMarker = false;
#endif
    private const bool EnableHoverHighlight = false;

    private enum OverlayState
    {
        AwaitingOcrSelection,
        SelectingOcrArea,
        OcrAreaSelected,
        OcrAreaResizing,
        OcrAreaMoving,
        ProcessingOcr,
        ReadyForTextSelection,
        SelectingText
    }

    private enum SearchPanelMode
    {
        Search,
        Translate
    }

    private readonly MonitorInfo _monitor;
    private readonly BitmapSource _screenshotSource;
    private readonly System.Drawing.Bitmap _screenshotBitmap;
    private readonly SettingsService _settingsService;
    private OverlayState _state = OverlayState.AwaitingOcrSelection;

    private Point? _dragStartDip;
    private Rect? _selectionStartDipRect;
    private Rect? _selectionDipRect;
    private Int32Rect? _selectionPixelRect;
    private int _selectionVersion;
    private ResizeHandle _activeHandle = ResizeHandle.None;

    private Storyboard? _processingStoryboard;

    private readonly List<OcrWord> _words = new();
    private readonly HashSet<int> _selectedWordIndices = new();
    private readonly HashSet<int> _selectedLineIndices = new();
    private readonly List<Rect> _wordRectsDip = new();
    private readonly List<double> _wordLuminanceByIndex = new();
    private int? _hoverWordIndex;
    private bool _isClosing;
    private bool _suppressHoverHighlight;
    private bool _hasChipTheme;
    private bool _chipThemeIsLight;
    private Task? _webViewInitTask;
    private bool _isSearchPanelOpen;
    private SearchPanelMode _searchPanelMode = SearchPanelMode.Search;
    private int _toastRequestId;
    private Action? _toastAction;
    private int _webSearchStatusRequestId;
    private WebView2? _searchWebView;
    private bool _webViewEventsHooked;
    private readonly SettingsViewModel _settingsViewModel = new();
    private uint _pendingHotkeyModifiers;
    private uint _pendingHotkeyKey;

    // Auto-close after a successful copy to enable a fast "copy-and-go" UX.
    private static readonly bool AutoCloseOnCopy = true;
    private const int ActionBarDelayMs = 150;

    private int _actionBarRequestId;
    private DateTime _lastSelectionChangeUtc = DateTime.UtcNow;

    internal event Action<Int32Rect, Rect, int>? ConfirmOcrRequested;
    internal event Action<Int32Rect, System.Drawing.Bitmap>? SaveScreenshotRequested;
    internal event Action? CancelRequested;

    private enum ResizeHandle
    {
        None,
        N,
        S,
        E,
        W,
        NW,
        NE,
        SW,
        SE
    }

    internal FrozenOverlayWindow(
        BitmapSource screenshot,
        System.Drawing.Bitmap screenshotBitmap,
        MonitorInfo monitor,
        SettingsService settingsService)
    {
        InitializeComponent();

        _monitor = monitor;
        _screenshotSource = screenshot;
        _screenshotBitmap = screenshotBitmap;
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        BackgroundImage.Source = screenshot;

        // WPF window coords are in DIPs; monitor bounds are in pixels.
        Left = monitor.PixelLeft / monitor.ScaleX;
        Top = monitor.PixelTop / monitor.ScaleY;
        Width = monitor.PixelWidth / monitor.ScaleX;
        Height = monitor.PixelHeight / monitor.ScaleY;
        WindowState = WindowState.Normal;

        KeyDown += OnKeyDown;
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;
        Closed += OnWindowClosed;

        // We attach to the window so selection works anywhere.
        AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(OnMouseLeftButtonDown), handledEventsToo: true);
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMove), handledEventsToo: true);
        AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler(OnMouseLeftButtonUp), handledEventsToo: true);

        DataContext = _settingsViewModel;
        _settingsViewModel.CurrentHotkeyDisplay = (Application.Current as App)?.GetCurrentHotkeyDisplay()
            ?? "Ctrl + Alt + Space";
        _pendingHotkeyModifiers = (Application.Current as App)?.GetHotkeyModifiers() ?? 0;
        _pendingHotkeyKey = (Application.Current as App)?.GetHotkeyKey() ?? 0x20;
        _settingsViewModel.EnableMouseChord = (Application.Current as App)?.GetEnableMouseChord() ?? true;
        _settingsViewModel.ThemeMode = (Application.Current as App)?.GetThemeMode() ?? ThemeMode.System;
        LoadOcrLanguageOptions();
        LoadWebSearchSettings();

        #if DEBUG
        if (ShowBuildMarker)
        {
            var exePath = Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            var pid = Environment.ProcessId;
            var time = DateTime.Now.ToString("HH:mm:ss");
            var fileName = System.IO.Path.GetFileName(exePath);
            var dirName = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(exePath) ?? string.Empty);
            BuildMarkerText.Text = $"Build {version} | {dirName}\\{fileName} | pid {pid} | {time}";
            BuildMarker.Visibility = Visibility.Visible;
        }
#endif
    }

    internal void ApplyOcrLayout(OcrLayout layout, int selectionVersion)
    {
        if (_selectionDipRect is null || _selectionPixelRect is null)
        {
            return;
        }
        if (selectionVersion != _selectionVersion)
        {
            return; // stale OCR result
        }

        StopProcessingAnimation();
        SelectionHandleCanvas.Visibility = Visibility.Collapsed;
        SelectionActionBar.Visibility = Visibility.Collapsed;

        _words.Clear();
        _words.AddRange(layout.Words);
        BuildOcrLayerWithinSelection(_selectionDipRect.Value);
        RefreshVisualStyles();

        _state = OverlayState.ReadyForTextSelection;

        // IMPORTANT: Keep OCR text fully opaque (do not animate parent Opacity; it multiplies text alpha).
        OcrVisualCanvas.BeginAnimation(OpacityProperty, null);
        OcrHitCanvas.BeginAnimation(OpacityProperty, null);
        OcrVisualCanvas.Opacity = 1;
        OcrHitCanvas.Opacity = 1;

        // If we want a transition, do it inside the render layer on chip fills only.
        OcrVisualCanvas.StartChipFadeIn();
        OcrHitCanvas.IsHitTestVisible = true;
    }

    private void BuildOcrLayerWithinSelection(Rect selectionDipRect)
    {
        OcrVisualCanvas.Reset();
        OcrHitCanvas.Children.Clear();
        _selectedWordIndices.Clear();
        _selectedLineIndices.Clear();
        _wordRectsDip.Clear();
        _wordLuminanceByIndex.Clear();
        _hoverWordIndex = null;

        // Clip OCR canvases to the selection area (so nothing "alive" spills outside).
        var clip = new RectangleGeometry(selectionDipRect, radiusX: 6, radiusY: 6);
        OcrVisualCanvas.Clip = clip;
        OcrHitCanvas.Clip = clip;

        // Cache word rectangles and background luminance for rendering.
        _wordRectsDip.Capacity = _words.Count;
        _wordLuminanceByIndex.Capacity = _words.Count;

        if (_selectionPixelRect is Int32Rect selectionPx)
        {
            UpdateChipTheme(selectionPx);
        }

        for (var i = 0; i < _words.Count; i++)
        {
            var w = _words[i];
            if (string.IsNullOrWhiteSpace(w.Text) || w.Width <= 0 || w.Height <= 0)
            {
                _wordRectsDip.Add(Rect.Empty);
                _wordLuminanceByIndex.Add(0.5);
                continue;
            }

            var xDip = selectionDipRect.X + (w.X / _monitor.ScaleX);
            var yDip = selectionDipRect.Y + (w.Y / _monitor.ScaleY);
            var wDip = w.Width / _monitor.ScaleX;
            var hDip = w.Height / _monitor.ScaleY;
            var rectDip = new Rect(xDip, yDip, wDip, hDip);
            _wordRectsDip.Add(rectDip);

            var luminance = 0.5;
            if (_selectionPixelRect is Int32Rect selPx)
            {
                var px = selPx.X + (int)Math.Round(w.X);
                var py = selPx.Y + (int)Math.Round(w.Y);
                var pw = (int)Math.Round(w.Width);
                var ph = (int)Math.Round(w.Height);
                luminance = GetCenterLuminance(new Int32Rect(px, py, pw, ph));
            }
            _wordLuminanceByIndex.Add(luminance);

            var hit = new Border
            {
                Width = wDip,
                Height = hDip,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                Tag = new WordHit(WordIndex: i, LineIndex: w.LineIndex),
                Cursor = Cursors.IBeam,
                IsHitTestVisible = true,
            };

            // IMPORTANT: never index into _words here (it can be cleared during re-selection).
            hit.MouseEnter += (_, _) =>
            {
                if (hit.Tag is WordHit h) ApplyHoverWord(h.WordIndex);
            };
            hit.MouseLeave += (_, _) =>
            {
                if (hit.Tag is WordHit h) ClearHoverWord(h.WordIndex);
            };

            Canvas.SetLeft(hit, xDip);
            Canvas.SetTop(hit, yDip);
            OcrHitCanvas.Children.Add(hit);
        }

        OcrVisualCanvas.SetWords(_words, _wordRectsDip, _wordLuminanceByIndex, _chipThemeIsLight);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_settingsViewModel.IsSettingsOpen)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CloseSettingsModal();
            }
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (_isSearchPanelOpen)
            {
                CloseSearchPanel();
                return;
            }
            CloseSafely();
        }

        if (_state is OverlayState.OcrAreaSelected or OverlayState.OcrAreaMoving or OverlayState.OcrAreaResizing)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ConfirmOcrSelection();
                return;
            }
        }

        if (_state is OverlayState.ReadyForTextSelection or OverlayState.SelectingText
            && e.Key == Key.C
            && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            if (CopySelectionToClipboard() && AutoCloseOnCopy)
            {
                CloseSafely();
            }
        }

        if (_state is OverlayState.ReadyForTextSelection or OverlayState.SelectingText
            && e.Key == Key.A
            && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            SelectAll();
            ShowActionBarNearSelection();
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Don't start selection when clicking on the close button or the action bar.
        if (IsWithin(CloseButton, e.OriginalSource)
            || IsWithin(ActionBar, e.OriginalSource)
            || IsWithin(SearchPanelRoot, e.OriginalSource)
            || IsWithin(HudContainer, e.OriginalSource)
            || IsWithin(SelectionActionBar, e.OriginalSource)
            || IsWithin(ToastBorder, e.OriginalSource)
            || _settingsViewModel.IsSettingsOpen)
        {
            return;
        }

        var p = e.GetPosition(this);

        if (_state is OverlayState.OcrAreaSelected)
        {
            if (TryGetResizeHandle(e.OriginalSource, out var handle))
            {
                _activeHandle = handle;
                _selectionStartDipRect = _selectionDipRect;
                _dragStartDip = p;
                _state = OverlayState.OcrAreaResizing;
                CaptureMouse();
                return;
            }

            if (_selectionDipRect is Rect sel && sel.Contains(p))
            {
                _activeHandle = ResizeHandle.None;
                _selectionStartDipRect = _selectionDipRect;
                _dragStartDip = p;
                _state = OverlayState.OcrAreaMoving;
                CaptureMouse();
                return;
            }

            _state = OverlayState.SelectingOcrArea;
            _dragStartDip = p;
            CaptureMouse();

            ClearSelectionVisuals();
            ResetOcrLayer();
            ShowSelection(new Rect(p, new Size(0, 0)));
            return;
        }

        if (_state is OverlayState.ReadyForTextSelection or OverlayState.SelectingText)
        {
            // If click is outside the current OCR selection, start a NEW OCR selection.
            if (_selectionDipRect is Rect sel && !sel.Contains(p))
            {
                _state = OverlayState.SelectingOcrArea;
                _dragStartDip = p;
                CaptureMouse();

                ClearSelectionVisuals();
                ResetOcrLayer();

                ShowSelection(new Rect(p, new Size(0, 0)));
                return;
            }

            // Click on a word selects; drag selects multiple.
            if (TryGetWordHitFromOriginalSource(e.OriginalSource, out var hit))
            {
                // Validate indices (can be stale in rare edge cases).
                if (hit.WordIndex >= 0 && hit.WordIndex < _words.Count)
                {
                    if (e.ClickCount >= 2)
                    {
                        SelectLine(hit.LineIndex);
                    }
                    else
                    {
                        SelectSingleWord(hit.WordIndex);
                    }
                    ShowActionBarNearSelection();
                }
                return;
            }

            _state = OverlayState.SelectingText;
            _dragStartDip = p;
            CaptureMouse();
            return;
        }

        // Otherwise we are selecting OCR area (selection-first flow).
        _state = OverlayState.SelectingOcrArea;
        _dragStartDip = p;
        CaptureMouse();

        // Reset any previous OCR layer.
        ClearSelectionVisuals();
        ResetOcrLayer();

        ShowSelection(new Rect(p, new Size(0, 0)));
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStartDip is null || !IsMouseCaptured)
        {
            if (_state == OverlayState.OcrAreaSelected)
            {
                UpdateSelectionCursor(e.GetPosition(this));
            }
            return;
        }

        var current = e.GetPosition(this);
        var rectDip = MakeRectDip(_dragStartDip.Value, current);

        if (_state == OverlayState.SelectingOcrArea)
        {
            ShowSelection(rectDip);
            return;
        }

        if (_state == OverlayState.OcrAreaMoving && _selectionStartDipRect is Rect startRect)
        {
            var dx = current.X - _dragStartDip.Value.X;
            var dy = current.Y - _dragStartDip.Value.Y;
            var moved = new Rect(startRect.X + dx, startRect.Y + dy, startRect.Width, startRect.Height);
            UpdateSelectionFromDip(moved);
            return;
        }

        if (_state == OverlayState.OcrAreaResizing && _selectionStartDipRect is Rect resizeStart)
        {
            var resized = ResizeFromHandle(resizeStart, _dragStartDip.Value, current, _activeHandle,
                keepAspect: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift),
                fromCenter: Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
            UpdateSelectionFromDip(resized);
            return;
        }

        if (_state == OverlayState.SelectingText && _selectionDipRect is Rect sel)
        {
            rectDip = IntersectRect(rectDip, sel);
            UpdateWordSelectionFromRect(rectDip);
            return;
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStartDip is null)
        {
            return;
        }

        var start = _dragStartDip.Value;
        var end = e.GetPosition(this);
        var rectDip = MakeRectDip(start, end);

        _dragStartDip = null;
        ReleaseMouseCapture();

        if (_state == OverlayState.SelectingOcrArea)
        {
            CommitSelection(rectDip);
            return;
        }

        if (_state is OverlayState.OcrAreaMoving or OverlayState.OcrAreaResizing)
        {
            _state = OverlayState.OcrAreaSelected;
            _activeHandle = ResizeHandle.None;
            _selectionStartDipRect = null;
            if (_selectionDipRect is Rect sel)
            {
                UpdateSelectionFromDip(sel);
            }
            return;
        }

        if (_state == OverlayState.SelectingText)
        {
            _state = OverlayState.ReadyForTextSelection;
            if (_selectionDipRect is Rect sel)
            {
                rectDip = IntersectRect(rectDip, sel);
                UpdateWordSelectionFromRect(rectDip);
            }
            if (_selectedWordIndices.Count > 0)
            {
                ShowActionBarNearSelection();
                return;
            }
            HideActionBar();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_settingsViewModel.IsCapturingHotkey)
        {
            return;
        }

        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            _settingsViewModel.IsCapturingHotkey = false;
            _settingsViewModel.StatusMessage = string.Empty;
            return;
        }

        if (HotkeyCaptureHelper.TryCapture(e, out var modifiers, out var key, out var error))
        {
            _pendingHotkeyModifiers = modifiers;
            _pendingHotkeyKey = key;
            _settingsViewModel.CurrentHotkeyDisplay = HotkeyCaptureHelper.FormatHotkey(modifiers, key);
            _settingsViewModel.StatusMessage = string.Empty;
            _settingsViewModel.IsCapturingHotkey = false;
        }
        else
        {
            _settingsViewModel.StatusMessage = error ?? "Invalid hotkey.";
        }
    }

    private static Rect MakeRectDip(Point a, Point b)
    {
        var x1 = Math.Min(a.X, b.X);
        var y1 = Math.Min(a.Y, b.Y);
        var x2 = Math.Max(a.X, b.X);
        var y2 = Math.Max(a.Y, b.Y);
        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }

    private void UpdateWordSelectionFromRect(Rect rectDip)
    {
        if (_words.Count == 0 || _selectionDipRect is null)
        {
            return;
        }

        _selectedWordIndices.Clear();
        _selectedLineIndices.Clear();

        // Select words by intersection (dip-space).
        for (var i = 0; i < _words.Count; i++)
        {
            var w = _words[i];
            if (string.IsNullOrWhiteSpace(w.Text) || w.Width <= 0 || w.Height <= 0)
            {
                continue;
            }
            var xDip = _selectionDipRect.Value.X + (w.X / _monitor.ScaleX);
            var yDip = _selectionDipRect.Value.Y + (w.Y / _monitor.ScaleY);
            var wDip = w.Width / _monitor.ScaleX;
            var hDip = w.Height / _monitor.ScaleY;
            var wr = new Rect(xDip, yDip, wDip, hDip);
            if (!wr.IsEmpty && rectDip.IntersectsWith(wr))
            {
                _selectedWordIndices.Add(i);
                _selectedLineIndices.Add(w.LineIndex);
            }
        }

        MarkSelectionChanged();
        RefreshVisualStyles();
    }

    private void SelectSingleWord(int index)
    {
        if (index < 0 || index >= _words.Count)
        {
            return;
        }

        _selectedWordIndices.Clear();
        _selectedWordIndices.Add(index);
        _selectedLineIndices.Clear();
        _selectedLineIndices.Add(_words[index].LineIndex);
        MarkSelectionChanged();
        RefreshVisualStyles();
    }

    private void SelectLine(int lineIndex)
    {
        _selectedWordIndices.Clear();
        _selectedLineIndices.Clear();
        _selectedLineIndices.Add(lineIndex);

        for (var i = 0; i < _words.Count; i++)
        {
            var w = _words[i];
            if (w.LineIndex == lineIndex && !string.IsNullOrWhiteSpace(w.Text))
            {
                _selectedWordIndices.Add(i);
            }
        }

        MarkSelectionChanged();
        RefreshVisualStyles();
    }

    private string? GetSelectedText()
    {
        if (_selectedWordIndices.Count == 0)
        {
            return null;
        }

        // Natural order: line index, then word index.
        var selectedWords = new List<OcrWord>(_selectedWordIndices.Count);
        foreach (var idx in _selectedWordIndices)
        {
            if (idx < 0 || idx >= _words.Count)
            {
                continue;
            }
            var w = _words[idx];
            if (!string.IsNullOrWhiteSpace(w.Text))
            {
                selectedWords.Add(w);
            }
        }

        if (selectedWords.Count == 0)
        {
            return null;
        }

        selectedWords.Sort((a, b) =>
        {
            var c = a.LineIndex.CompareTo(b.LineIndex);
            return c != 0 ? c : a.WordIndex.CompareTo(b.WordIndex);
        });

        var lines = new List<string>();
        var currentLine = -1;
        var line = new List<string>();

        foreach (var w in selectedWords)
        {
            if (w.LineIndex != currentLine)
            {
                if (line.Count > 0)
                {
                    lines.Add(string.Join(' ', line));
                    line.Clear();
                }
                currentLine = w.LineIndex;
            }
            if (!string.IsNullOrWhiteSpace(w.Text))
            {
                line.Add(w.Text);
            }
        }
        if (line.Count > 0)
        {
            lines.Add(string.Join(' ', line));
        }

        var text = string.Join(Environment.NewLine, lines);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private bool CopySelectionToClipboard()
    {
        var text = BuildSelectedCopyText();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            Clipboard.SetText(text);
            _hoverWordIndex = null;
            _suppressHoverHighlight = true;
            RefreshVisualStyles();
            return true;
        }
        catch
        {
            // Clipboard can fail if locked by another process; keep overlay open.
            Log.Error("Clipboard write failed during copy action.");
            return false;
        }
    }

    private string? BuildSelectedCopyText()
    {
        if (_selectedWordIndices.Count == 0)
        {
            return null;
        }

        var items = new List<(OcrWord Word, Rect Rect)>(_selectedWordIndices.Count);
        foreach (var idx in _selectedWordIndices)
        {
            if (idx < 0 || idx >= _words.Count || idx >= _wordRectsDip.Count)
            {
                continue;
            }

            var rect = _wordRectsDip[idx];
            if (rect.IsEmpty)
            {
                continue;
            }

            var w = _words[idx];
            if (string.IsNullOrWhiteSpace(w.Text))
            {
                continue;
            }

            items.Add((w, rect));
        }

        if (items.Count == 0)
        {
            return null;
        }

        var text = OcrTextFormatter.BuildCopyText(items);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    internal void CloseSafely()
    {
        if (_isClosing)
        {
            return;
        }
        _isClosing = true;
        Log.Info("Overlay close requested");
        Close();
    }

    private void SelectAll()
    {
        _selectedWordIndices.Clear();
        _selectedLineIndices.Clear();
        for (var i = 0; i < _words.Count; i++)
        {
            var w = _words[i];
            if (string.IsNullOrWhiteSpace(w.Text)) continue;
            _selectedWordIndices.Add(i);
            _selectedLineIndices.Add(w.LineIndex);
        }
        MarkSelectionChanged();
        RefreshVisualStyles();
    }

    private void ApplyHoverWord(int wordIndex)
    {
        // Always track hover (even on selected words for Selected+Hover state)
        _hoverWordIndex = wordIndex;
        RefreshVisualStyles();
    }

    private void ClearHoverWord(int wordIndex)
    {
        if (_hoverWordIndex == wordIndex)
        {
            _hoverWordIndex = null;
            RefreshVisualStyles();
        }
    }

    private void RefreshVisualStyles()
    {
        OcrVisualCanvas.UpdateState(_selectedWordIndices, _hoverWordIndex, ShouldRenderHover());
    }

    private void MarkSelectionChanged()
    {
        _lastSelectionChangeUtc = DateTime.UtcNow;
        _suppressHoverHighlight = _selectedWordIndices.Count > 0 || _state == OverlayState.SelectingText;
        // Keep hover tracked; render logic will handle Selected+Hover vs Hover-only
    }

    private bool ShouldRenderHover()
    {
        return EnableHoverHighlight
            && !_suppressHoverHighlight
            && _selectedWordIndices.Count == 0
            && _state != OverlayState.SelectingText;
    }

    private void UpdateChipTheme(Int32Rect selectionPx)
    {
        var median = GetSelectionMedianLuminance(selectionPx, gridSize: 24);

        if (!_hasChipTheme)
        {
            _chipThemeIsLight = median > 0.60;
            _hasChipTheme = true;
            return;
        }

        if (median > 0.65)
        {
            _chipThemeIsLight = true;
        }
        else if (median < 0.55)
        {
            _chipThemeIsLight = false;
        }
    }

    private double GetSelectionMedianLuminance(Int32Rect rect, int gridSize)
    {
        var width = _screenshotSource.PixelWidth;
        var height = _screenshotSource.PixelHeight;

        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return 0.5;
        }

        var x0 = Math.Clamp(rect.X, 0, Math.Max(0, width - 1));
        var y0 = Math.Clamp(rect.Y, 0, Math.Max(0, height - 1));
        var x1 = Math.Clamp(rect.X + rect.Width, 0, width);
        var y1 = Math.Clamp(rect.Y + rect.Height, 0, height);
        var w = Math.Max(1, x1 - x0);
        var h = Math.Max(1, y1 - y0);

        var samplesX = Math.Max(1, Math.Min(gridSize, w));
        var samplesY = Math.Max(1, Math.Min(gridSize, h));
        var totalSamples = samplesX * samplesY;
        var samples = new double[totalSamples];

        var idx = 0;
        for (var gy = 0; gy < samplesY; gy++)
        {
            var fy = (gy + 0.5) / samplesY;
            var py = y0 + (int)Math.Round(fy * (h - 1));
            for (var gx = 0; gx < samplesX; gx++)
            {
                var fx = (gx + 0.5) / samplesX;
                var px = x0 + (int)Math.Round(fx * (w - 1));
                samples[idx++] = GetPixelLuminance(px, py);
            }
        }

        Array.Sort(samples);
        return samples.Length == 0 ? 0.5 : samples[samples.Length / 2];
    }

    private double GetPixelLuminance(int x, int y)
    {
        var width = _screenshotSource.PixelWidth;
        var height = _screenshotSource.PixelHeight;

        if (width == 0 || height == 0)
        {
            return 0.5;
        }

        var px = Math.Clamp(x, 0, width - 1);
        var py = Math.Clamp(y, 0, height - 1);
        var rect = new Int32Rect(px, py, 1, 1);
        var buffer = new byte[4];
        try
        {
            _screenshotSource.CopyPixels(rect, buffer, 4, 0);
        }
        catch
        {
            return 0.5;
        }

        var b = buffer[0];
        var g = buffer[1];
        var r = buffer[2];
        return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
    }

    private double GetCenterLuminance(Int32Rect rect)
    {
        var width = _screenshotSource.PixelWidth;
        var height = _screenshotSource.PixelHeight;

        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return 0.5;
        }

        var cx = rect.X + (rect.Width / 2);
        var cy = rect.Y + (rect.Height / 2);
        var radius = 1; // 3x3 sampling

        var x = Math.Max(0, cx - radius);
        var y = Math.Max(0, cy - radius);
        var w = Math.Min((radius * 2) + 1, width - x);
        var h = Math.Min((radius * 2) + 1, height - y);

        if (w <= 0 || h <= 0)
        {
            return 0.5;
        }

        var sampleRect = new Int32Rect(x, y, w, h);
        var stride = sampleRect.Width * 4;
        var buffer = new byte[stride * sampleRect.Height];
        try
        {
            _screenshotSource.CopyPixels(sampleRect, buffer, stride, 0);
        }
        catch
        {
            return 0.5;
        }

        double sum = 0;
        var count = 0;

        for (var py = 0; py < sampleRect.Height; py++)
        {
            var row = py * stride;
            for (var px = 0; px < sampleRect.Width; px++)
            {
                var i = row + (px * 4);
                if (i + 2 >= buffer.Length)
                {
                    continue;
                }
                var b = buffer[i];
                var g = buffer[i + 1];
                var r = buffer[i + 2];
                var l = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                sum += l;
                count++;
            }
        }

        return count > 0 ? sum / count : 0.5;
    }

    private static bool TryGetWordHitFromOriginalSource(object? originalSource, out WordHit hit)
    {
        if (originalSource is not DependencyObject d)
        {
            hit = default;
            return false;
        }

        while (d != null)
        {
            if (d is Border { Tag: WordHit h })
            {
                hit = h;
                return true;
            }

            var parent = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
            if (parent is null)
            {
                if (d is FrameworkElement fe && fe.TemplatedParent is DependencyObject tp)
                {
                    d = tp;
                    continue;
                }
                if (d is FrameworkContentElement fce && fce.TemplatedParent is DependencyObject tp2)
                {
                    d = tp2;
                    continue;
                }
                break;
            }
            d = parent;
        }

        hit = default;
        return false;
    }

    private static bool IsWithin(DependencyObject container, object? originalSource)
    {
        try
        {
            if (originalSource is not DependencyObject d) return false;
            while (d != null)
            {
                if (ReferenceEquals(d, container)) return true;
                if (d is FrameworkElement fe && ReferenceEquals(fe.TemplatedParent, container)) return true;
                if (d is FrameworkContentElement fce && ReferenceEquals(fce.TemplatedParent, container)) return true;

                var parent = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
                if (parent is null)
                {
                    if (d is FrameworkElement fe2 && fe2.TemplatedParent is DependencyObject tp)
                    {
                        d = tp;
                        continue;
                    }
                    if (d is FrameworkContentElement fce2 && fce2.TemplatedParent is DependencyObject tp2)
                    {
                        d = tp2;
                        continue;
                    }
                    break;
                }
                d = parent;
            }
        }
        catch
        {
            // never crash due to hit testing
        }
        return false;
    }

    private void ShowActionBarNearSelection()
    {
        if (_selectedWordIndices.Count == 0)
        {
            HideActionBar();
            return;
        }
        if (_selectionDipRect is null)
        {
            HideActionBar();
            return;
        }
        var requestId = Interlocked.Increment(ref _actionBarRequestId);
        _ = ShowActionBarWhenStableAsync(requestId);
    }

    private void HideActionBar()
    {
        if (ActionBar.Visibility != Visibility.Visible)
        {
            ActionBar.Visibility = Visibility.Collapsed;
            return;
        }

        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (_, _) => ActionBar.Visibility = Visibility.Collapsed;
        ActionBar.BeginAnimation(OpacityProperty, fadeOut);
    }

    private async Task ShowActionBarWhenStableAsync(int requestId)
    {
        var elapsed = (DateTime.UtcNow - _lastSelectionChangeUtc).TotalMilliseconds;
        var delay = Math.Max(0, ActionBarDelayMs - (int)elapsed);
        if (delay > 0)
        {
            await Task.Delay(delay);
        }

        if (requestId != _actionBarRequestId)
        {
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            if (requestId != _actionBarRequestId)
            {
                return;
            }
            if (_selectedWordIndices.Count == 0 || _selectionDipRect is null)
            {
                HideActionBar();
                return;
            }

            PositionActionBarNearSelection();
            FadeInActionBar();
        });
    }

    private void PositionActionBarNearSelection()
    {
        if (_selectionDipRect is not Rect selection)
        {
            HideActionBar();
            return;
        }

        // Compute selection bounds in DIPs from selected words.
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;

        foreach (var idx in _selectedWordIndices)
        {
            if (idx < 0 || idx >= _words.Count) continue;
            var w = _words[idx];
            minX = Math.Min(minX, selection.X + (w.X / _monitor.ScaleX));
            minY = Math.Min(minY, selection.Y + (w.Y / _monitor.ScaleY));
            maxX = Math.Max(maxX, selection.X + ((w.X + w.Width) / _monitor.ScaleX));
            maxY = Math.Max(maxY, selection.Y + ((w.Y + w.Height) / _monitor.ScaleY));
        }

        if (double.IsInfinity(minX) || double.IsInfinity(minY))
        {
            HideActionBar();
            return;
        }

        ActionBar.Visibility = Visibility.Visible;
        ActionBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var barW = ActionBar.DesiredSize.Width;
        var barH = ActionBar.DesiredSize.Height;

        // Prefer below selection; clamp into window.
        var x = Math.Min(Math.Max(minX, 12), Math.Max(12, ActualWidth - barW - 12));
        var y = maxY + 10;
        if (y + barH > ActualHeight - 12)
        {
            y = Math.Max(12, minY - barH - 10);
        }

        Canvas.SetLeft(ActionBar, x);
        Canvas.SetTop(ActionBar, y);
    }

    private void FadeInActionBar()
    {
        ActionBar.Opacity = 0;
        ActionBar.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SearchPanelCloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseSearchPanel();
    }

    private void SearchBackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_searchWebView?.CanGoBack == true)
        {
            _searchWebView.GoBack();
            UpdateSearchNavButtons();
        }
    }

    private void SearchForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_searchWebView?.CanGoForward == true)
        {
            _searchWebView.GoForward();
            UpdateSearchNavButtons();
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (CopySelectionToClipboard())
        {
            if (AutoCloseOnCopy)
            {
                CloseSafely();
                return;
            }
            HideActionBar();
            return;
        }
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedText = GetSelectedText();
        var query = selectedText is null ? string.Empty : NormalizeSearchQuery(selectedText);
        if (string.IsNullOrWhiteSpace(query))
        {
            ShowToast("Select text to search");
            return;
        }

        await OpenSearchPanelAsync(query, SearchPanelMode.Search);
        HideActionBar();
    }

    private void TranslateButton_Click(object sender, RoutedEventArgs e)
    {
        _ = TranslateSelectionAsync();
    }

    private async Task TranslateSelectionAsync()
    {
        var selectedText = GetSelectedText();
        var query = selectedText is null ? string.Empty : NormalizeTranslateText(selectedText);
        if (string.IsNullOrWhiteSpace(query))
        {
            ShowToast("Select text to translate");
            return;
        }

        await OpenSearchPanelAsync(query, SearchPanelMode.Translate);
        HideActionBar();
    }

    private void ClearWordSelection()
    {
        _selectedWordIndices.Clear();
        _selectedLineIndices.Clear();
        _hoverWordIndex = null;
        MarkSelectionChanged();
        RefreshVisualStyles();
        HideActionBar();
    }

    private void ResetOcrLayer()
    {
        OcrVisualCanvas.Reset();
        OcrHitCanvas.Children.Clear();
        OcrVisualCanvas.Opacity = 0;
        OcrHitCanvas.Opacity = 0;
        OcrHitCanvas.IsHitTestVisible = false;
        _words.Clear();
        _wordRectsDip.Clear();
        _wordLuminanceByIndex.Clear();
        ClearWordSelection();
    }

    private void ShowSelection(Rect rectDip)
    {
        _selectionDipRect = rectDip;
        UpdateSelectionVisuals(rectDip);
        UpdateDimOutside(rectDip);
    }

    private void CommitSelection(Rect rectDip)
    {
        ResetOcrLayer();
        UpdateSelectionFromDip(rectDip);

        if (_selectionPixelRect is null)
        {
            ClearSelectionVisuals();
            _state = OverlayState.AwaitingOcrSelection;
            return;
        }

        _state = OverlayState.OcrAreaSelected;
        ShowSelectionActionBar();
        if (_selectionDipRect is Rect sel)
        {
            UpdateSelectionHandles(sel);
            UpdateSelectionActionBarPosition(sel);
        }
    }

    private void UpdateSelectionVisuals(Rect rectDip)
    {
        SelectionBorder.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionBorder, rectDip.X);
        Canvas.SetTop(SelectionBorder, rectDip.Y);
        SelectionBorder.Width = rectDip.Width;
        SelectionBorder.Height = rectDip.Height;
        UpdateSelectionHandles(rectDip);
        UpdateSelectionActionBarPosition(rectDip);
    }

    private void ClearSelectionVisuals()
    {
        SelectionBorder.Visibility = Visibility.Collapsed;
        ProcessingBorder.Visibility = Visibility.Collapsed;
        ProcessingShimmer.Visibility = Visibility.Collapsed;
        SelectionHandleCanvas.Visibility = Visibility.Collapsed;
        SelectionActionBar.Visibility = Visibility.Collapsed;
        DimTop.Visibility = Visibility.Collapsed;
        DimLeft.Visibility = Visibility.Collapsed;
        DimRight.Visibility = Visibility.Collapsed;
        DimBottom.Visibility = Visibility.Collapsed;
        _selectionDipRect = null;
        _selectionPixelRect = null;
        _hasChipTheme = false;
        StopProcessingAnimation();
        HideActionBar();
    }

    private void UpdateSelectionFromDip(Rect rectDip)
    {
        var clamped = ClampSelectionRect(rectDip);
        _selectionDipRect = clamped;
        UpdateSelectionPixelRect(clamped);
        UpdateSelectionVisuals(clamped);
        UpdateDimOutside(clamped);
    }

    private void UpdateSelectionPixelRect(Rect rectDip)
    {
        var px = (int)Math.Round(rectDip.X * _monitor.ScaleX);
        var py = (int)Math.Round(rectDip.Y * _monitor.ScaleY);
        var pw = (int)Math.Round(rectDip.Width * _monitor.ScaleX);
        var ph = (int)Math.Round(rectDip.Height * _monitor.ScaleY);

        if (px < 0) { pw += px; px = 0; }
        if (py < 0) { ph += py; py = 0; }
        if (px + pw > _monitor.PixelWidth) { pw = _monitor.PixelWidth - px; }
        if (py + ph > _monitor.PixelHeight) { ph = _monitor.PixelHeight - py; }

        pw = Math.Max(0, pw);
        ph = Math.Max(0, ph);

        if (pw < 16 || ph < 16)
        {
            _selectionPixelRect = null;
            return;
        }

        _selectionPixelRect = new Int32Rect(px, py, pw, ph);
    }

    private Rect ClampSelectionRect(Rect rectDip)
    {
        var minW = 16 / _monitor.ScaleX;
        var minH = 16 / _monitor.ScaleY;

        var x = rectDip.X;
        var y = rectDip.Y;
        var w = Math.Max(minW, rectDip.Width);
        var h = Math.Max(minH, rectDip.Height);

        if (x < 0) x = 0;
        if (y < 0) y = 0;
        if (x + w > ActualWidth) w = Math.Max(minW, ActualWidth - x);
        if (y + h > ActualHeight) h = Math.Max(minH, ActualHeight - y);

        return new Rect(x, y, w, h);
    }

    private void UpdateSelectionHandles(Rect rectDip)
    {
        if (_state is not (OverlayState.OcrAreaSelected or OverlayState.OcrAreaMoving or OverlayState.OcrAreaResizing))
        {
            SelectionHandleCanvas.Visibility = Visibility.Collapsed;
            return;
        }

        SelectionHandleCanvas.Visibility = Visibility.Visible;
        const double size = 8;
        var half = size / 2;

        SetHandle(HandleNW, rectDip.Left - half, rectDip.Top - half);
        SetHandle(HandleN, rectDip.Left + (rectDip.Width / 2) - half, rectDip.Top - half);
        SetHandle(HandleNE, rectDip.Right - half, rectDip.Top - half);
        SetHandle(HandleE, rectDip.Right - half, rectDip.Top + (rectDip.Height / 2) - half);
        SetHandle(HandleSE, rectDip.Right - half, rectDip.Bottom - half);
        SetHandle(HandleS, rectDip.Left + (rectDip.Width / 2) - half, rectDip.Bottom - half);
        SetHandle(HandleSW, rectDip.Left - half, rectDip.Bottom - half);
        SetHandle(HandleW, rectDip.Left - half, rectDip.Top + (rectDip.Height / 2) - half);
    }

    private static void SetHandle(FrameworkElement handle, double x, double y)
    {
        Canvas.SetLeft(handle, x);
        Canvas.SetTop(handle, y);
    }

    private void ShowSelectionActionBar()
    {
        SelectionActionBar.Visibility = Visibility.Visible;
        if (_selectionDipRect is Rect rectDip)
        {
            UpdateSelectionActionBarPosition(rectDip);
        }
    }

    private void UpdateSelectionActionBarPosition(Rect rectDip)
    {
        if (SelectionActionBar.Visibility != Visibility.Visible)
        {
            return;
        }

        SelectionActionBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = SelectionActionBar.DesiredSize;
        var margin = 10;

        var belowY = rectDip.Bottom + margin;
        var aboveY = rectDip.Top - margin - size.Height;

        var x = rectDip.Left;
        var y = belowY;

        if (belowY + size.Height > ActualHeight && aboveY >= 0)
        {
            y = aboveY;
        }
        else if (belowY + size.Height > ActualHeight && aboveY < 0)
        {
            var leftSpace = rectDip.Left - margin - size.Width;
            var rightSpace = ActualWidth - rectDip.Right - margin;
            if (rightSpace >= size.Width)
            {
                x = rectDip.Right + margin;
                y = rectDip.Top;
            }
            else if (leftSpace >= 0)
            {
                x = rectDip.Left - margin - size.Width;
                y = rectDip.Top;
            }
            else
            {
                y = Math.Max(0, ActualHeight - size.Height - margin);
            }
        }

        x = Math.Max(0, Math.Min(x, ActualWidth - size.Width));
        y = Math.Max(0, Math.Min(y, ActualHeight - size.Height));

        Canvas.SetLeft(SelectionActionBar, x);
        Canvas.SetTop(SelectionActionBar, y);
    }

    private void UpdateSelectionCursor(Point p)
    {
        if (_selectionDipRect is not Rect rect)
        {
            Cursor = Cursors.Arrow;
            return;
        }

        if (TryGetResizeHandleAtPoint(p, rect, out var handle))
        {
            Cursor = handle switch
            {
                ResizeHandle.N or ResizeHandle.S => Cursors.SizeNS,
                ResizeHandle.E or ResizeHandle.W => Cursors.SizeWE,
                ResizeHandle.NE or ResizeHandle.SW => Cursors.SizeNESW,
                ResizeHandle.NW or ResizeHandle.SE => Cursors.SizeNWSE,
                _ => Cursors.Arrow
            };
            return;
        }

        Cursor = rect.Contains(p) ? Cursors.SizeAll : Cursors.Arrow;
    }

    private bool TryGetResizeHandle(object? originalSource, out ResizeHandle handle)
    {
        handle = ResizeHandle.None;
        if (originalSource is not DependencyObject d)
        {
            return false;
        }

        while (d != null)
        {
            if (d is FrameworkElement fe && fe.Tag is string tag)
            {
                if (Enum.TryParse(tag, out ResizeHandle parsed))
                {
                    handle = parsed;
                    return true;
                }
            }
            d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
        }

        return false;
    }

    private bool TryGetResizeHandleAtPoint(Point p, Rect rect, out ResizeHandle handle)
    {
        const double size = 10;
        var half = size / 2;

        var handles = new Dictionary<ResizeHandle, Rect>
        {
            [ResizeHandle.NW] = new Rect(rect.Left - half, rect.Top - half, size, size),
            [ResizeHandle.N] = new Rect(rect.Left + rect.Width / 2 - half, rect.Top - half, size, size),
            [ResizeHandle.NE] = new Rect(rect.Right - half, rect.Top - half, size, size),
            [ResizeHandle.E] = new Rect(rect.Right - half, rect.Top + rect.Height / 2 - half, size, size),
            [ResizeHandle.SE] = new Rect(rect.Right - half, rect.Bottom - half, size, size),
            [ResizeHandle.S] = new Rect(rect.Left + rect.Width / 2 - half, rect.Bottom - half, size, size),
            [ResizeHandle.SW] = new Rect(rect.Left - half, rect.Bottom - half, size, size),
            [ResizeHandle.W] = new Rect(rect.Left - half, rect.Top + rect.Height / 2 - half, size, size)
        };

        foreach (var pair in handles)
        {
            if (pair.Value.Contains(p))
            {
                handle = pair.Key;
                return true;
            }
        }

        handle = ResizeHandle.None;
        return false;
    }

    private Rect ResizeFromHandle(Rect startRect, Point startPoint, Point current, ResizeHandle handle, bool keepAspect, bool fromCenter)
    {
        var dx = current.X - startPoint.X;
        var dy = current.Y - startPoint.Y;

        var rect = startRect;
        switch (handle)
        {
            case ResizeHandle.N:
                rect = new Rect(rect.X, rect.Y + dy, rect.Width, rect.Height - dy);
                break;
            case ResizeHandle.S:
                rect = new Rect(rect.X, rect.Y, rect.Width, rect.Height + dy);
                break;
            case ResizeHandle.W:
                rect = new Rect(rect.X + dx, rect.Y, rect.Width - dx, rect.Height);
                break;
            case ResizeHandle.E:
                rect = new Rect(rect.X, rect.Y, rect.Width + dx, rect.Height);
                break;
            case ResizeHandle.NW:
                rect = new Rect(rect.X + dx, rect.Y + dy, rect.Width - dx, rect.Height - dy);
                break;
            case ResizeHandle.NE:
                rect = new Rect(rect.X, rect.Y + dy, rect.Width + dx, rect.Height - dy);
                break;
            case ResizeHandle.SW:
                rect = new Rect(rect.X + dx, rect.Y, rect.Width - dx, rect.Height + dy);
                break;
            case ResizeHandle.SE:
                rect = new Rect(rect.X, rect.Y, rect.Width + dx, rect.Height + dy);
                break;
        }

        if (fromCenter)
        {
            var center = new Point(startRect.X + startRect.Width / 2, startRect.Y + startRect.Height / 2);
            var halfW = Math.Abs(rect.Width) / 2;
            var halfH = Math.Abs(rect.Height) / 2;
            rect = new Rect(center.X - halfW, center.Y - halfH, halfW * 2, halfH * 2);
        }

        if (keepAspect)
        {
            var aspect = startRect.Width / Math.Max(1, startRect.Height);
            if (rect.Width / rect.Height > aspect)
            {
                var newW = rect.Height * aspect;
                rect = new Rect(rect.X, rect.Y, newW, rect.Height);
            }
            else
            {
                var newH = rect.Width / aspect;
                rect = new Rect(rect.X, rect.Y, rect.Width, newH);
            }
        }

        return rect;
    }

    private void ConfirmOcrSelection()
    {
        if (_selectionPixelRect is null || _selectionDipRect is null)
        {
            return;
        }

        _selectionVersion++;
        _state = OverlayState.ProcessingOcr;
        SelectionHandleCanvas.Visibility = Visibility.Collapsed;
        SelectionActionBar.Visibility = Visibility.Collapsed;
        StartProcessingAnimation(_selectionDipRect.Value);

        ConfirmOcrRequested?.Invoke(_selectionPixelRect.Value, _selectionDipRect.Value, _selectionVersion);
    }

    private void UpdateDimOutside(Rect rectDip)
    {
        DimTop.Visibility = Visibility.Visible;
        DimLeft.Visibility = Visibility.Visible;
        DimRight.Visibility = Visibility.Visible;
        DimBottom.Visibility = Visibility.Visible;

        // Top
        Canvas.SetLeft(DimTop, 0);
        Canvas.SetTop(DimTop, 0);
        DimTop.Width = ActualWidth;
        DimTop.Height = Math.Max(0, rectDip.Y);

        // Left
        Canvas.SetLeft(DimLeft, 0);
        Canvas.SetTop(DimLeft, rectDip.Y);
        DimLeft.Width = Math.Max(0, rectDip.X);
        DimLeft.Height = rectDip.Height;

        // Right
        Canvas.SetLeft(DimRight, rectDip.X + rectDip.Width);
        Canvas.SetTop(DimRight, rectDip.Y);
        DimRight.Width = Math.Max(0, ActualWidth - (rectDip.X + rectDip.Width));
        DimRight.Height = rectDip.Height;

        // Bottom
        Canvas.SetLeft(DimBottom, 0);
        Canvas.SetTop(DimBottom, rectDip.Y + rectDip.Height);
        DimBottom.Width = ActualWidth;
        DimBottom.Height = Math.Max(0, ActualHeight - (rectDip.Y + rectDip.Height));
    }

    private void StartProcessingAnimation(Rect rectDip)
    {
        ProcessingBorder.Visibility = Visibility.Visible;
        ProcessingShimmer.Visibility = Visibility.Visible;

        Canvas.SetLeft(ProcessingBorder, rectDip.X);
        Canvas.SetTop(ProcessingBorder, rectDip.Y);
        ProcessingBorder.Width = rectDip.Width;
        ProcessingBorder.Height = rectDip.Height;

        Canvas.SetLeft(ProcessingShimmer, rectDip.X);
        Canvas.SetTop(ProcessingShimmer, rectDip.Y);
        ProcessingShimmer.Width = rectDip.Width;
        ProcessingShimmer.Height = rectDip.Height;

        var borderBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            Opacity = 0.8
        };
        borderBrush.GradientStops.Add(new GradientStop(Color.FromArgb(40, 234, 245, 234), 0));
        borderBrush.GradientStops.Add(new GradientStop(Color.FromArgb(140, 234, 245, 234), 0.5));
        borderBrush.GradientStops.Add(new GradientStop(Color.FromArgb(40, 234, 245, 234), 1));
        var borderTransform = new TranslateTransform(-1, 0);
        borderBrush.RelativeTransform = borderTransform;
        ProcessingBorder.BorderBrush = borderBrush;

        var shimmerBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            Opacity = 0.22
        };
        shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 234, 245, 234), 0));
        shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(90, 234, 245, 234), 0.5));
        shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 234, 245, 234), 1));
        var shimmerTransform = new TranslateTransform(-1, 0);
        shimmerBrush.RelativeTransform = shimmerTransform;
        ProcessingShimmer.Fill = shimmerBrush;

        _processingStoryboard?.Stop(this);
        _processingStoryboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

        var anim = new DoubleAnimation(-1, 1, TimeSpan.FromMilliseconds(850))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(anim, ProcessingBorder);
        Storyboard.SetTargetProperty(anim, new PropertyPath("BorderBrush.(LinearGradientBrush.RelativeTransform).(TranslateTransform.X)"));
        _processingStoryboard.Children.Add(anim);

        var anim2 = new DoubleAnimation(-1, 1, TimeSpan.FromMilliseconds(850))
        {
            BeginTime = TimeSpan.FromMilliseconds(120),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(anim2, ProcessingShimmer);
        Storyboard.SetTargetProperty(anim2, new PropertyPath("Fill.(LinearGradientBrush.RelativeTransform).(TranslateTransform.X)"));
        _processingStoryboard.Children.Add(anim2);

        _processingStoryboard.Begin(this, true);
    }

    private void StopProcessingAnimation()
    {
        _processingStoryboard?.Stop(this);
        _processingStoryboard = null;
        ProcessingBorder.Visibility = Visibility.Collapsed;
        ProcessingShimmer.Visibility = Visibility.Collapsed;
    }

    private static Rect IntersectRect(Rect a, Rect b)
    {
        a.Intersect(b);
        return a;
    }
}


internal sealed class OcrRenderLayer : FrameworkElement
{
    private static readonly Typeface ChipTypeface = new("Segoe UI");
    private static bool HighContrastLock = false; // TEMP: baseline verification toggle
    private static readonly bool RenderChips = false; // disable chip fill/border to keep text readable

    private readonly record struct SelectionRun(Rect Rect, int LineIndex, double AvgLuminance);

    private IReadOnlyList<OcrWord> _words = Array.Empty<OcrWord>();
    private IReadOnlyList<Rect> _wordRects = Array.Empty<Rect>();
    private IReadOnlyList<double> _wordLuminance = Array.Empty<double>();
    private bool _chipThemeIsLight = true;
    private HashSet<int> _selectedWords = new();
    private int? _hoverWordIndex;
    private bool _hoverAllowed;
    private int? _hoverAnimatedIndex;
    private double _hoverProgress;
    private bool _hoverTargetVisible;
    private double _chipFadeProgress = 1.0;
    private bool _chipFadeTargetVisible = true;
    private TimeSpan _lastFrameTime;
    private bool _isHoverAnimating;

    public void SetWords(IReadOnlyList<OcrWord> words, IReadOnlyList<Rect> wordRects, IReadOnlyList<double> wordLuminance, bool chipThemeIsLight)
    {
        _words = words;
        _wordRects = wordRects;
        _wordLuminance = wordLuminance;
        _chipThemeIsLight = chipThemeIsLight;
        InvalidateVisual();
    }

    public void UpdateState(IReadOnlyCollection<int> selectedWordIndices, int? hoverWordIndex, bool hoverAllowed)
    {
        _selectedWords = selectedWordIndices as HashSet<int> ?? new HashSet<int>(selectedWordIndices);
        _hoverWordIndex = hoverWordIndex;
        _hoverAllowed = hoverAllowed;
        UpdateHoverAnimationTarget();
        InvalidateVisual();
    }

    public void Reset()
    {
        _words = Array.Empty<OcrWord>();
        _wordRects = Array.Empty<Rect>();
        _wordLuminance = Array.Empty<double>();
        _selectedWords = new HashSet<int>();
        _hoverWordIndex = null;
        _hoverAnimatedIndex = null;
        _hoverProgress = 0;
        _hoverTargetVisible = false;
        _chipFadeProgress = 1.0;
        _chipFadeTargetVisible = true;
        StopHoverAnimation();
        InvalidateVisual();
    }

    public void StartChipFadeIn()
    {
        if (HighContrastLock)
        {
            // Baseline mode: no fading, maximize readability.
            _chipFadeProgress = 1.0;
            _chipFadeTargetVisible = true;
            StopHoverAnimation();
            return;
        }

        _chipFadeProgress = 0;
        _chipFadeTargetVisible = true;
        StartHoverAnimation();
        InvalidateVisual();
    }

    private void UpdateHoverAnimationTarget()
    {
        var shouldShowHover = _hoverAllowed && _hoverWordIndex.HasValue && _selectedWords.Count == 0;
        if (shouldShowHover)
        {
            if (_hoverAnimatedIndex != _hoverWordIndex)
            {
                _hoverAnimatedIndex = _hoverWordIndex;
                _hoverProgress = 0;
            }
            _hoverTargetVisible = true;
            StartHoverAnimation();
        }
        else
        {
            _hoverTargetVisible = false;
            if (_hoverProgress > 0)
            {
                StartHoverAnimation();
            }
            else
            {
                _hoverAnimatedIndex = null;
                StopHoverAnimation();
            }
        }
    }

    private void StartHoverAnimation()
    {
        if (_isHoverAnimating)
        {
            return;
        }

        _isHoverAnimating = true;
        _lastFrameTime = TimeSpan.Zero;
        CompositionTarget.Rendering += OnHoverRendering;
    }

    private void StopHoverAnimation()
    {
        if (!_isHoverAnimating)
        {
            return;
        }

        CompositionTarget.Rendering -= OnHoverRendering;
        _isHoverAnimating = false;
    }

    private void OnHoverRendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs args)
        {
            return;
        }

        if (_lastFrameTime == TimeSpan.Zero)
        {
            _lastFrameTime = args.RenderingTime;
            return;
        }

        var delta = args.RenderingTime - _lastFrameTime;
        _lastFrameTime = args.RenderingTime;

        var durationMs = _hoverTargetVisible ? 150.0 : 120.0;
        var step = delta.TotalMilliseconds / durationMs;
        if (_hoverTargetVisible)
        {
            _hoverProgress = Math.Min(1.0, _hoverProgress + step);
        }
        else
        {
            _hoverProgress = Math.Max(0.0, _hoverProgress - step);
        }

        // Chip fade-in (fills/borders only; never affects text).
        if (_chipFadeTargetVisible && _chipFadeProgress < 1.0)
        {
            var chipStep = delta.TotalMilliseconds / 160.0;
            _chipFadeProgress = Math.Min(1.0, _chipFadeProgress + chipStep);
        }

        if (_hoverProgress == 0 && !_hoverTargetVisible)
        {
            _hoverAnimatedIndex = null;
            if (_chipFadeProgress >= 1.0)
            {
                StopHoverAnimation();
            }
        }
        else if (_hoverProgress == 1.0 && _hoverTargetVisible)
        {
            if (_chipFadeProgress >= 1.0)
            {
                StopHoverAnimation();
            }
        }
        else if (_chipFadeProgress >= 1.0 && (_hoverProgress == 0 && !_hoverTargetVisible))
        {
            StopHoverAnimation();
        }

        InvalidateVisual();
    }

    private static double GetColorLuminance(Color c)
    {
        return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
    }

    private Dictionary<int, double> BuildLineMedianLuminance()
    {
        var perLine = new Dictionary<int, List<double>>();
        for (var i = 0; i < _words.Count && i < _wordLuminance.Count; i++)
        {
            var rect = _wordRects[i];
            if (rect.IsEmpty)
            {
                continue;
            }

            var lineIndex = _words[i].LineIndex;
            if (!perLine.TryGetValue(lineIndex, out var list))
            {
                list = new List<double>();
                perLine[lineIndex] = list;
            }
            list.Add(_wordLuminance[i]);
        }

        var medians = new Dictionary<int, double>();
        foreach (var pair in perLine)
        {
            var values = pair.Value;
            values.Sort();
            medians[pair.Key] = values.Count == 0 ? 0.5 : values[values.Count / 2];
        }

        return medians;
    }

    private List<SelectionRun> BuildSelectionRuns(double padX, double padY, double gapThreshold)
    {
        var perLine = new Dictionary<int, List<(Rect Rect, double Luminance)>>();

        foreach (var index in _selectedWords)
        {
            if (index < 0 || index >= _wordRects.Count || index >= _words.Count)
            {
                continue;
            }

            var rect = _wordRects[index];
            if (rect.IsEmpty)
            {
                continue;
            }

            var word = _words[index];
            var padded = new Rect(rect.X - padX, rect.Y - padY, rect.Width + (padX * 2), rect.Height + (padY * 2));
            if (!perLine.TryGetValue(word.LineIndex, out var list))
            {
                list = new List<(Rect, double)>();
                perLine[word.LineIndex] = list;
            }
            var luminance = index < _wordLuminance.Count ? _wordLuminance[index] : 0.5;
            list.Add((padded, luminance));
        }

        var runs = new List<SelectionRun>();
        foreach (var pair in perLine)
        {
            var rects = pair.Value;
            rects.Sort((a, b) => a.Rect.X.CompareTo(b.Rect.X));
            if (rects.Count == 0)
            {
                continue;
            }

            var currentRect = rects[0].Rect;
            var luminanceSum = rects[0].Luminance;
            var luminanceCount = 1;

            for (var i = 1; i < rects.Count; i++)
            {
                var next = rects[i].Rect;
                var gap = next.X - currentRect.Right;
                if (gap <= gapThreshold)
                {
                    currentRect.Union(next);
                    luminanceSum += rects[i].Luminance;
                    luminanceCount++;
                }
                else
                {
                    runs.Add(new SelectionRun(currentRect, pair.Key, luminanceSum / luminanceCount));
                    currentRect = next;
                    luminanceSum = rects[i].Luminance;
                    luminanceCount = 1;
                }
            }
            runs.Add(new SelectionRun(currentRect, pair.Key, luminanceSum / luminanceCount));
        }

        runs.Sort((a, b) =>
        {
            var y = a.Rect.Y.CompareTo(b.Rect.Y);
            return y != 0 ? y : a.Rect.X.CompareTo(b.Rect.X);
        });

        return runs;
    }

    private static byte AdjustChipAlpha(byte baseAlpha, double luminance, bool lightTheme)
    {
        const double maxAdjust = 0.0; // disabled for stability (optional later: chip-only tweak)
        var delta = (luminance - 0.5) * (maxAdjust * 2);
        if (!lightTheme)
        {
            delta = -delta;
        }

        var adjusted = (baseAlpha / 255.0) + delta;

        if (HighContrastLock)
        {
            return baseAlpha;
        }

        // Contrast safety boost without changing theme (chip fill only).
        var boost = 0.0;
        if (lightTheme && luminance >= 0.75)
        {
            boost = Math.Clamp((luminance - 0.75) * 0.4, 0.08, 0.15);
        }
        else if (!lightTheme && luminance <= 0.25)
        {
            boost = Math.Clamp((0.25 - luminance) * 0.4, 0.08, 0.15);
        }

        adjusted += boost;

        // Stronger ranges for guaranteed readability.
        var min = lightTheme ? 0.88 : 0.60;
        var max = lightTheme ? 0.94 : 0.72;
        adjusted = Math.Clamp(adjusted, min, max);
        return (byte)Math.Round(adjusted * 255);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_words.Count == 0 || _wordRects.Count == 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        const double padX = 3;
        const double padY = 1.5;
        const double radius = 5.5;
        const double selectionPadX = 1.5;
        const double selectionPadY = 1.0;
        const double selectionGapThreshold = 8.0;
        const double selectionRadius = 2.5;
        const double selectionInflateX = 2.0;
        const double selectionInflateY = 1.0;

        // Baseline (HighContrastLock) constants:
        // - Fill: white @ 0.92
        // - Border: white @ 0.20
        // - Text: #111111 @ 1.0
        const byte highContrastFillAlpha = 234; // 0.92
        const byte highContrastBorderAlpha = 51; // 0.20
        var highContrastBorder = new SolidColorBrush(Color.FromArgb(highContrastBorderAlpha, 255, 255, 255));
        var highContrastText = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11));

        // Adaptive theming (safe ranges; text always full opacity).
        const byte lightChipFillBase = 235; // 0.92 (clamped 0.88–0.94)
        var lightChipBorder = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)); // 14%
        var lightText = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11));

        const byte darkChipFillBase = 173; // 0.68 (clamped 0.60–0.72)
        var darkChipBorder = new SolidColorBrush(Color.FromArgb(54, 255, 255, 255)); // 21%
        var darkText = new SolidColorBrush(Color.FromRgb(255, 255, 255));

        var accent = SystemParameters.WindowGlassColor;
        var chipOpacity = HighContrastLock ? 1.0 : Math.Clamp(_chipFadeProgress, 0.0, 1.0);
        var hasSelection = _selectedWords.Count > 0;
        var selectionOutline = new SolidColorBrush(Color.FromArgb(242, accent.R, accent.G, accent.B)); // ~0.95
        var selectionOutlineHover = new SolidColorBrush(Color.FromArgb(255, accent.R, accent.G, accent.B)); // 1.0
        var selectionInnerStroke = new SolidColorBrush(Color.FromArgb(38, accent.R, accent.G, accent.B)); // ~0.15
        var selectionGlow = new SolidColorBrush(Color.FromArgb(56, accent.R, accent.G, accent.B)); // ~0.22

        // Precompute border pens with fade applied (never affects text).
        var lightBorderPen = new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Round(36 * chipOpacity), 255, 255, 255)), 0.9);
        var darkBorderPen = new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Round(54 * chipOpacity), 255, 255, 255)), 0.9);
        var highContrastBorderPen = new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Round(highContrastBorderAlpha * chipOpacity), 255, 255, 255)), 0.9);
        var lineMedianLuminance = BuildLineMedianLuminance();

        if (hasSelection)
        {
            var runs = BuildSelectionRuns(selectionPadX, selectionPadY, selectionGapThreshold);
            var hoverSelected = _hoverWordIndex.HasValue && _selectedWords.Contains(_hoverWordIndex.Value);
            var hoverWordRect = Rect.Empty;
            var hoverLineIndex = -1;
            if (hoverSelected)
            {
                var hw = _hoverWordIndex!.Value;
                if (hw >= 0 && hw < _wordRects.Count && hw < _words.Count)
                {
                    var rect = _wordRects[hw];
                    hoverWordRect = new Rect(rect.X - selectionPadX, rect.Y - selectionPadY, rect.Width + (selectionPadX * 2), rect.Height + (selectionPadY * 2));
                    hoverLineIndex = _words[hw].LineIndex;
                }
            }

            foreach (var run in runs)
            {
                var outlineRect = new Rect(
                    run.Rect.X - selectionInflateX,
                    run.Rect.Y - selectionInflateY,
                    run.Rect.Width + (selectionInflateX * 2),
                    run.Rect.Height + (selectionInflateY * 2));

                var isHoverRun = hoverSelected
                    && hoverLineIndex == run.LineIndex
                    && outlineRect.IntersectsWith(hoverWordRect);

                // Selection wash disabled to keep text fully readable.

                if (isHoverRun)
                {
                    // Outer glow: expanded outline
                    var glowRect = new Rect(
                        outlineRect.X - 2,
                        outlineRect.Y - 2,
                        outlineRect.Width + 4,
                        outlineRect.Height + 4);
                    dc.DrawRoundedRectangle(null, new Pen(selectionGlow, 2.0), glowRect, selectionRadius + 2, selectionRadius + 2);
                    dc.DrawRoundedRectangle(null, new Pen(selectionOutlineHover, 2.2), outlineRect, selectionRadius, selectionRadius);
                }
                else
                {
                    dc.DrawRoundedRectangle(null, new Pen(selectionOutline, 1.6), outlineRect, selectionRadius, selectionRadius);
                }

                // Optional inner stroke for crispness.
                dc.DrawRoundedRectangle(null, new Pen(selectionInnerStroke, 0.8), outlineRect, selectionRadius, selectionRadius);
            }
        }

        for (var i = 0; i < _words.Count; i++)
        {
            if (i >= _wordRects.Count)
            {
                break;
            }

            var word = _words[i];
            var rect = _wordRects[i];
            if (rect.IsEmpty || string.IsNullOrWhiteSpace(word.Text))
            {
                continue;
            }

            var chipRect = new Rect(rect.X - padX, rect.Y - padY, rect.Width + (padX * 2), rect.Height + (padY * 2));
            if (chipRect.Width <= 0 || chipRect.Height <= 0)
            {
                continue;
            }

            var luminance = i < _wordLuminance.Count ? _wordLuminance[i] : 0.5;
            var isLightTheme = _chipThemeIsLight;

            Brush chipBorder;
            Brush textBrush;
            byte fillAlpha;
            if (HighContrastLock)
            {
                chipBorder = highContrastBorder;
                textBrush = highContrastText; // full opacity by brush (no parent opacity)
                fillAlpha = highContrastFillAlpha;
            }
            else
            {
                chipBorder = isLightTheme ? lightChipBorder : darkChipBorder;
                var baseAlpha = isLightTheme ? lightChipFillBase : darkChipFillBase;
                fillAlpha = AdjustChipAlpha(baseAlpha, luminance, isLightTheme);
                textBrush = lightText; // placeholder; actual brush decided after fill alpha
            }

            if (!hasSelection)
            {
                if (RenderChips)
                {
                    // Apply fade ONLY to chip visuals (never to text).
                    fillAlpha = (byte)Math.Round(fillAlpha * chipOpacity);
                    var borderPen = HighContrastLock
                        ? highContrastBorderPen
                        : (isLightTheme ? lightBorderPen : darkBorderPen);

                    var chipFill = new SolidColorBrush((HighContrastLock || isLightTheme)
                        ? Color.FromArgb(fillAlpha, 255, 255, 255)
                        : Color.FromArgb(fillAlpha, 0, 0, 0));

                    // Subtle shadow (Snipping-like softness).
                    byte shadowAlpha = (byte)(isLightTheme ? 12 : 10);
                    shadowAlpha = (byte)Math.Round(shadowAlpha * chipOpacity);
                    var shadowColor = Color.FromArgb(shadowAlpha, 0, 0, 0);
                    var shadowBrush = new SolidColorBrush(shadowColor);
                    var shadowRect = new Rect(chipRect.X, chipRect.Y + 1, chipRect.Width, chipRect.Height);
                    dc.DrawRoundedRectangle(shadowBrush, null, shadowRect, radius, radius);

                    dc.DrawRoundedRectangle(chipFill, borderPen, chipRect, radius, radius);

                    if (!HighContrastLock)
                    {
                        // Choose text color for best contrast against the composite background
                        var a = fillAlpha / 255.0;
                        var compositeL = isLightTheme
                            ? (a * 1.0) + ((1.0 - a) * luminance)
                            : ((1.0 - a) * luminance);
                        textBrush = compositeL > 0.6 ? lightText : darkText;
                    }
                }
                else
                {
                    // No chips: choose text color directly from background luminance
                    var lineLuminance = lineMedianLuminance.TryGetValue(word.LineIndex, out var lineLum)
                        ? lineLum
                        : luminance;
                    textBrush = lineLuminance > 0.6 ? lightText : darkText;
                }
            }

            var isSelected = _selectedWords.Contains(i);
            var isHoverOnSelected = _hoverAllowed && isSelected && _hoverWordIndex == i;
            var isHoverOnly = _hoverAllowed && _hoverAnimatedIndex == i && !isSelected && _selectedWords.Count == 0 && _hoverProgress > 0;

            if (hasSelection && !isSelected)
            {
                // When selection exists, leave unselected words as screenshot pixels.
                continue;
            }

            if (!hasSelection && isHoverOnly)
            {
                // Hover (only when no selection exists)
                var hoverBorderAlpha = (byte)Math.Round(40 * _hoverProgress);
                var hoverBorder = new SolidColorBrush(Color.FromArgb(hoverBorderAlpha, accent.R, accent.G, accent.B)); // 16%
                dc.DrawRoundedRectangle(null, new Pen(hoverBorder, 1.0), chipRect, radius, radius);
            }

            var fontSize = Math.Max(11, rect.Height * 0.9);
            var lineLuminanceSelected = lineMedianLuminance.TryGetValue(word.LineIndex, out var lineLumSelected)
                ? lineLumSelected
                : luminance;
            var selectedTextBrush = lineLuminanceSelected > 0.6
                ? new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11))
                : new SolidColorBrush(Color.FromRgb(255, 255, 255));

            var ft = new FormattedText(
                word.Text,
                CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight,
                ChipTypeface,
                fontSize,
                isSelected ? selectedTextBrush : textBrush,
                dpi);
            ft.MaxTextWidth = Math.Max(0, rect.Width);
            ft.MaxTextHeight = Math.Max(0, rect.Height);

            var textY = rect.Y + Math.Max(0, (rect.Height - ft.Height) / 2);
            var textX = rect.X;

            // Subtle text shadow to preserve readability.
            var textShadowAlpha = isSelected ? (byte)96 : (byte)36;
            var textShadowColor = Color.FromArgb(textShadowAlpha, 0, 0, 0);
            var textShadow = new SolidColorBrush(textShadowColor);
            var shadowOffset = isSelected ? 0.7 : 0.4;
            dc.DrawText(ft, new Point(textX, textY + shadowOffset));
            if (isSelected)
            {
                dc.DrawText(ft, new Point(textX + 0.6, textY + 0.2));
            }
            dc.DrawText(ft, new Point(textX, textY));
        }
    }
}
