using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

using FrozenOCR.Core;
using FrozenOCR.Settings;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FrozenOCR.Overlay;

internal partial class FrozenOverlayWindow
{
    private static string NormalizeSearchQuery(string text)
    {
        return System.Text.RegularExpressions.Regex.Replace(text.Trim(), "\\s+", " ");
    }

    private static string NormalizeTranslateText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalizedLines = text
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n');

        var paragraphs = new System.Collections.Generic.List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var rawLine in normalizedLines)
        {
            var line = System.Text.RegularExpressions.Regex.Replace(rawLine.Trim(), "\\s+", " ");
            if (line.Length == 0)
            {
                if (current.Length > 0)
                {
                    paragraphs.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            if (current.Length > 0)
            {
                var joinsNaturally =
                    !current.ToString().EndsWith(':') &&
                    !current.ToString().EndsWith(';') &&
                    !current.ToString().EndsWith('.') &&
                    !current.ToString().EndsWith('!') &&
                    !current.ToString().EndsWith('?');

                current.Append(joinsNaturally ? " " : "\n");
            }

            current.Append(line);
        }

        if (current.Length > 0)
        {
            paragraphs.Add(current.ToString());
        }

        return string.Join("\n\n", paragraphs).Trim();
    }

    internal void ShowToast(string message, string? actionLabel = null, Action? action = null)
    {
        ToastText.Text = message;
        if (!string.IsNullOrWhiteSpace(actionLabel) && action is not null)
        {
            ToastActionButton.Content = actionLabel;
            ToastActionButton.Visibility = Visibility.Visible;
            _toastAction = action;
        }
        else
        {
            ToastActionButton.Visibility = Visibility.Collapsed;
            _toastAction = null;
        }
        ToastBorder.Visibility = Visibility.Visible;
        ToastBorder.Opacity = 0;
        ToastBorder.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));

        var requestId = ++_toastRequestId;
        _ = Task.Run(async () =>
        {
            await Task.Delay(1400);
            await Dispatcher.InvokeAsync(() =>
            {
                if (requestId != _toastRequestId)
                {
                    return;
                }
                var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
                fade.Completed += (_, _) =>
                {
                    if (requestId == _toastRequestId)
                    {
                        ToastBorder.Visibility = Visibility.Collapsed;
                    }
                };
                ToastBorder.BeginAnimation(OpacityProperty, fade);
            });
        });
    }

    private void ToastActionButton_Click(object sender, RoutedEventArgs e)
    {
        var action = _toastAction;
        _toastAction = null;
        ToastActionButton.Visibility = Visibility.Collapsed;
        e.Handled = true;
        action?.Invoke();
    }

    private Task EnsureWebViewReadyAsync()
    {
        if (_webViewInitTask is { IsCanceled: true } or { IsFaulted: true })
        {
            _webViewInitTask = null;
        }

        if (_webViewInitTask != null)
        {
            return _webViewInitTask;
        }

        _webViewInitTask = InitializeWebViewAsync();
        return _webViewInitTask;
    }

    private async Task InitializeWebViewAsync()
    {
        EnsureWebViewCreated();
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FrozenOCR",
            "WebView2");
        Directory.CreateDirectory(userDataFolder);

        _searchWebView!.CreationProperties ??= new CoreWebView2CreationProperties();
        _searchWebView.CreationProperties.UserDataFolder = userDataFolder;
        await _searchWebView.EnsureCoreWebView2Async();
        HookWebViewEvents();
        Log.Info("Search WebView initialized");
    }

    private void SearchWebView_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isSearchPanelOpen)
        {
            e.Handled = true;
            CloseSearchPanel();
        }
    }

    private async Task OpenSearchPanelAsync(string query, SearchPanelMode mode)
    {
        try
        {
            await EnsureWebViewReadyAsync();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to initialize search panel: {ex}");
            ShowToast("Search panel unavailable");
            DisposeSearchWebView();
            return;
        }

        OpenSearchPanel();
        _searchPanelMode = mode;
        SearchPanelTitle.Text = mode == SearchPanelMode.Translate ? "Translate" : "Search";

        NavigateSearch(query);
        Log.Info($"Search panel opened mode={mode}");
    }

    private void CloseSearchPanel()
    {
        if (!_isSearchPanelOpen)
        {
            return;
        }

        _isSearchPanelOpen = false;
        SearchPanel.Visibility = Visibility.Collapsed;
        SearchPanelRoot.Visibility = Visibility.Collapsed;
        SearchPanelRoot.IsHitTestVisible = false;
        SearchPanelSplitter.Visibility = Visibility.Collapsed;
        SearchPanelSplitterColumn.Width = new GridLength(0);
        SearchPanelColumn.Width = new GridLength(0);
        Log.Info("Search panel closed");
    }

    private void StopWebViewMedia()
    {
        try
        {
            if (_searchWebView?.CoreWebView2 != null)
            {
                _searchWebView.CoreWebView2.Navigate("about:blank");
            }
        }
        catch
        {
            // Ignore if already disposing or unavailable
        }
    }

    private void DisposeSearchWebView()
    {
        var webView = _searchWebView;
        if (webView is null)
        {
            _webViewInitTask = null;
            _webViewEventsHooked = false;
            UpdateSearchNavButtons();
            return;
        }

        try
        {
            UnhookWebViewEvents();
            webView.PreviewKeyDown -= SearchWebView_OnPreviewKeyDown;
            webView.NavigationStarting -= SearchWebView_NavigationStarting;
            webView.NavigationCompleted -= SearchWebView_NavigationCompleted;
            webView.SourceChanged -= SearchWebView_SourceChanged;

            if (SearchWebViewHost.Children.Contains(webView))
            {
                SearchWebViewHost.Children.Remove(webView);
            }

            webView.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to dispose search WebView: {ex}");
        }
        finally
        {
            _searchWebView = null;
            _webViewInitTask = null;
            _webViewEventsHooked = false;
            UpdateSearchNavButtons();
            Log.Info("Search WebView disposed");
            Log.Memory("Search WebView disposed");
        }
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Normal;
        Activate();
        Log.Info($"Overlay loaded bounds={_monitor.PixelLeft},{_monitor.PixelTop} {_monitor.PixelWidth}x{_monitor.PixelHeight}");
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        StopWebViewMedia();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        DisposeSearchWebView();
        Log.Info("Overlay window closed");
    }

    private void NavigateSearch(string query)
    {
        var url = _searchPanelMode == SearchPanelMode.Translate
            ? SettingsService.BuildTranslateUrl(
                _settingsService.GetWebTranslateSettings(),
                SettingsService.ProviderBing,
                null,
                null,
                query)
            : SettingsService.BuildSearchUrl(_settingsService.GetWebSearchSettings(), null, query);
        EnsureWebViewCreated();
        _searchWebView!.Source = new Uri(url);
    }

    private void OpenSearchPanel()
    {
        if (_isSearchPanelOpen)
        {
            return;
        }

        _isSearchPanelOpen = true;
        SearchPanelRoot.Visibility = Visibility.Visible;
        SearchPanelRoot.IsHitTestVisible = true;
        SearchPanel.Visibility = Visibility.Visible;
        SearchPanelSplitter.Visibility = Visibility.Visible;
        SearchPanelSplitterColumn.Width = new GridLength(6);
        SearchPanelColumn.Width = new GridLength(460);
        UpdateSearchNavButtons();
    }

    private void EnsureWebViewCreated()
    {
        if (_searchWebView != null)
        {
            return;
        }

        _searchWebView = new WebView2
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _searchWebView.PreviewKeyDown += SearchWebView_OnPreviewKeyDown;
        _searchWebView.NavigationStarting += SearchWebView_NavigationStarting;
        _searchWebView.NavigationCompleted += SearchWebView_NavigationCompleted;
        _searchWebView.SourceChanged += SearchWebView_SourceChanged;
        SearchWebViewHost.Children.Add(_searchWebView);
    }

    private void UpdateSearchNavButtons()
    {
        if (_searchWebView is null)
        {
            SearchBackButton.IsEnabled = false;
            SearchForwardButton.IsEnabled = false;
            return;
        }

        SearchBackButton.IsEnabled = _searchWebView.CanGoBack;
        SearchForwardButton.IsEnabled = _searchWebView.CanGoForward;
    }

    private void HookWebViewEvents()
    {
        if (_searchWebView is null || _searchWebView.CoreWebView2 is null || _webViewEventsHooked)
        {
            return;
        }

        _webViewEventsHooked = true;
        _searchWebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
        _searchWebView.CoreWebView2.HistoryChanged += CoreWebView2_HistoryChanged;
    }

    private void UnhookWebViewEvents()
    {
        if (_searchWebView?.CoreWebView2 is null || !_webViewEventsHooked)
        {
            return;
        }

        _searchWebView.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
        _searchWebView.CoreWebView2.HistoryChanged -= CoreWebView2_HistoryChanged;
        _webViewEventsHooked = false;
    }

    private void SearchWebView_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!IsAllowedNavigation(e.Uri))
        {
            e.Cancel = true;
            ShowToast("Unsupported link");
        }
    }

    private void SearchWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        PersistTranslateLanguagesFromCurrentUri();
        UpdateSearchNavButtons();
    }

    private void SearchWebView_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        PersistTranslateLanguagesFromCurrentUri();
        UpdateSearchNavButtons();
    }

    private void CoreWebView2_HistoryChanged(object? sender, object e)
    {
        PersistTranslateLanguagesFromCurrentUri();
        UpdateSearchNavButtons();
    }

    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (string.IsNullOrWhiteSpace(e.Uri))
        {
            return;
        }

        if (!IsAllowedNavigation(e.Uri))
        {
            ShowToast("Unsupported link");
            return;
        }

        _searchWebView?.CoreWebView2?.Navigate(e.Uri);
    }

    private static bool IsAllowedNavigation(string? uriString)
    {
        if (string.IsNullOrWhiteSpace(uriString))
        {
            return true;
        }

        if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(uri.Scheme, "about", StringComparison.OrdinalIgnoreCase);
    }

    private void PersistTranslateLanguagesFromCurrentUri()
    {
        if (_searchPanelMode != SearchPanelMode.Translate)
        {
            return;
        }

        var uri = _searchWebView?.Source;
        if (uri is null)
        {
            return;
        }

        if (!string.Equals(uri.Host, "www.bing.com", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.Contains("translator", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var query = ParseQueryString(uri.Query);
        if (!query.TryGetValue("from", out var from) || string.IsNullOrWhiteSpace(from))
        {
            from = "auto";
        }

        if (!query.TryGetValue("to", out var to) || string.IsNullOrWhiteSpace(to))
        {
            return;
        }

        var current = _settingsService.GetWebTranslateSettings();
        if (string.Equals(current.From, from, StringComparison.OrdinalIgnoreCase)
            && string.Equals(current.To, to, StringComparison.OrdinalIgnoreCase)
            && string.Equals(current.DefaultProvider, SettingsService.ProviderBing, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _settingsService.UpdateWebTranslateSettings(current with
        {
            DefaultProvider = SettingsService.ProviderBing,
            From = from,
            To = to
        });
    }

    private static System.Collections.Generic.Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        var trimmed = query[0] == '?' ? query[1..] : query;
        var pairs = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value;
            }
        }

        return result;
    }
}
