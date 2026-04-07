using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Application = System.Windows.Application;

using FrozenOCR.Core;
using FrozenOCR.Input;
using FrozenOCR.Ocr;
using FrozenOCR.Ocr.Providers;
using FrozenOCR.Settings;

namespace FrozenOCR.Overlay;

internal partial class FrozenOverlayWindow
{
    private void SettingsGearButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsModal();
    }

    private void ConfirmOcrButton_Click(object sender, RoutedEventArgs e)
    {
        ConfirmOcrSelection();
    }

    private void SaveScreenshotButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectionPixelRect is Int32Rect px)
        {
            SaveScreenshotRequested?.Invoke(px, _screenshotBitmap);
        }
    }

    private void CancelSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke();
        CloseSafely();
    }

    private void OcrProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _settingsViewModel.IsCapturingHotkey)
        {
            return;
        }

        LoadOcrLanguageOptions();
    }

    private void OcrLanguageModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            SaveOcrSettings();
        }
    }

    private void OcrLanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            SaveOcrSettings();
        }
    }

    private void RefreshOcrLanguagesButton_Click(object sender, RoutedEventArgs e)
    {
        LoadOcrLanguageOptions();
        SaveOcrSettings();
    }

    private void EscPill_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_settingsViewModel.IsSettingsOpen)
        {
            CloseSettingsModal();
            return;
        }

        if (_isSearchPanelOpen)
        {
            CloseSearchPanel();
            return;
        }

        CloseSafely();
    }

    private void SettingsBackdrop_OnMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        CloseSettingsModal();
    }

    private void CloseSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        CloseSettingsModal();
    }

    private void ChangeHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        _settingsViewModel.IsCapturingHotkey = true;
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var app = Application.Current as App;
        if (app is null)
        {
            return;
        }

        if (!TryEnsureScreenshotFolderReady(_settingsViewModel.ScreenshotSaveFolder, out var folderError))
        {
            _settingsViewModel.WebSearchStatusMessage = folderError ?? "Screenshot folder is unavailable.";
            return;
        }

        if (!app.TryUpdateHotkey(_pendingHotkeyModifiers, _pendingHotkeyKey, out var hotkeyError))
        {
            _settingsViewModel.WebSearchStatusMessage = hotkeyError ?? "Failed to save hotkey.";
            return;
        }

        app.SetEnableMouseChord(_settingsViewModel.EnableMouseChord);
        app.SetThemeMode(_settingsViewModel.ThemeMode);

        SaveOcrSettings();

        var templates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [SettingsService.ProviderGoogle] = _settingsViewModel.GoogleTemplate?.Trim() ?? string.Empty,
            [SettingsService.ProviderBing] = _settingsViewModel.BingTemplate?.Trim() ?? string.Empty,
            [SettingsService.ProviderDuckDuckGo] = _settingsViewModel.DuckDuckGoTemplate?.Trim() ?? string.Empty
        };

        foreach (var pair in templates)
        {
            if (!TryValidateTemplate(pair.Key, pair.Value, out var error))
            {
                SetWebSearchStatus(error, autoClear: false);
                return;
            }
        }

        var defaultProvider = _settingsViewModel.WebSearchDefaultProvider;
        if (string.IsNullOrWhiteSpace(defaultProvider) || !templates.ContainsKey(defaultProvider))
        {
            defaultProvider = SettingsService.ProviderGoogle;
        }

        _settingsService.UpdateWebSearchSettings(new WebSearchSettings
        {
            DefaultProvider = defaultProvider,
            Providers = templates
        });

        SaveScreenshotSettings(_settingsService);

        SetWebSearchStatus("Settings saved.", autoClear: true);
        _settingsViewModel.CurrentHotkeyDisplay = app.GetCurrentHotkeyDisplay();
        CloseSettingsModal();
    }

    private void ResetWebSearchButton_Click(object sender, RoutedEventArgs e)
    {
        LoadWebSearchSettings();
        SetWebSearchStatus("Search templates reset.", autoClear: true);
    }

    private void ResetHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        var fallback = SettingsService.DefaultHotkey;
        _pendingHotkeyModifiers = fallback.Modifiers;
        _pendingHotkeyKey = fallback.Key;
        _settingsViewModel.CurrentHotkeyDisplay = HotkeyCaptureHelper.FormatHotkey(fallback.Modifiers, fallback.Key);
        _settingsViewModel.IsCapturingHotkey = false;
    }

    private void OpenSettingsModal()
    {
        _settingsViewModel.IsCapturingHotkey = false;
        _settingsViewModel.EnableMouseChord = (Application.Current as App)?.GetEnableMouseChord() ?? true;
        _settingsViewModel.ThemeMode = (Application.Current as App)?.GetThemeMode() ?? ThemeMode.System;
        _pendingHotkeyModifiers = (Application.Current as App)?.GetHotkeyModifiers() ?? SettingsService.DefaultHotkey.Modifiers;
        _pendingHotkeyKey = (Application.Current as App)?.GetHotkeyKey() ?? SettingsService.DefaultHotkey.Key;
        _settingsViewModel.CurrentHotkeyDisplay = HotkeyCaptureHelper.FormatHotkey(_pendingHotkeyModifiers, _pendingHotkeyKey);
        LoadOcrSettings();
        LoadOcrLanguageOptions();
        LoadWebSearchSettings();
        LoadScreenshotSettings();

        _settingsViewModel.IsSettingsOpen = true;
        SettingsOverlay.Visibility = Visibility.Visible;
        SettingsOverlay.Opacity = 0;
        SettingsModalScale.ScaleX = 0.92;
        SettingsModalScale.ScaleY = 0.92;
        SettingsOverlay.IsHitTestVisible = true;

        SettingsOverlay.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        });
        SettingsModalScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(0.92, 1.0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        });
        SettingsModalScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation(0.92, 1.0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        });
    }

    private void LoadOcrLanguageOptions()
    {
        var list = new List<OcrLanguageOption>
        {
            new(OcrSettings.Auto, "Auto (Windows language)")
        };

        try
        {
            var service = new OcrService(_settingsService);
            var langs = service.GetAvailableLanguages(_settingsViewModel.OcrProviderMode);
            foreach (var lang in langs)
            {
                list.Add(new OcrLanguageOption(lang.Tag, lang.DisplayName));
            }
        }
        catch
        {
            // Ignore if OCR language list isn't available.
        }

        list.Sort((a, b) =>
        {
            if (a.Tag == OcrSettings.Auto) return -1;
            if (b.Tag == OcrSettings.Auto) return 1;
            return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
        });

        _settingsViewModel.OcrLanguages = new ObservableCollection<OcrLanguageOption>(list);
    }

    private void LoadOcrSettings()
    {
        var settings = _settingsService.GetOcrSettings();
        _settingsViewModel.OcrProviderMode = settings.ProviderMode;
        _settingsViewModel.OcrLanguageMode = settings.LanguageMode;
        _settingsViewModel.OcrLanguageTag = settings.LanguageTag;
    }

    private void SaveOcrSettings()
    {
        _settingsService.UpdateOcrSettings(new OcrSettings
        {
            ProviderMode = _settingsViewModel.OcrProviderMode,
            LanguageMode = _settingsViewModel.OcrLanguageMode,
            LanguageTag = _settingsViewModel.OcrLanguageTag,
            PreprocessProfile = PreprocessProfile.Auto,
            RecognitionMode = RecognitionMode.Default
        });
        _settingsViewModel.OcrStatusMessage = "OCR settings saved.";
    }

    private void LoadWebSearchSettings()
    {
        var settings = _settingsService.GetWebSearchSettings();
        _settingsViewModel.WebSearchDefaultProvider = settings.DefaultProvider;
        _settingsViewModel.GoogleTemplate = GetProviderTemplate(settings, SettingsService.ProviderGoogle);
        _settingsViewModel.BingTemplate = GetProviderTemplate(settings, SettingsService.ProviderBing);
        _settingsViewModel.DuckDuckGoTemplate = GetProviderTemplate(settings, SettingsService.ProviderDuckDuckGo);
        _settingsViewModel.WebSearchAdvancedOpen = false;
        _settingsViewModel.WebSearchStatusMessage = string.Empty;
    }

    private void LoadScreenshotSettings()
    {
        var settings = _settingsService.GetScreenshotSettings();
        _settingsViewModel.ScreenshotSaveFolder = settings.SaveFolder;
    }

    private void SaveScreenshotSettings(SettingsService service)
    {
        var folder = _settingsViewModel.ScreenshotSaveFolder?.Trim() ?? string.Empty;
        service.UpdateScreenshotSettings(new ScreenshotSettings
        {
            SaveFolder = folder
        });
    }

    private static bool TryEnsureScreenshotFolderReady(string? folderText, out string? error)
    {
        error = null;
        var folder = string.IsNullOrWhiteSpace(folderText)
            ? ScreenshotExportService.GetDefaultFolder()
            : folderText.Trim();

        try
        {
            Directory.CreateDirectory(folder);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Screenshot folder is unavailable: {ex.Message}";
            return false;
        }
    }

    private void UseDefaultScreenshotFolderButton_Click(object sender, RoutedEventArgs e)
    {
        _settingsViewModel.ScreenshotSaveFolder = ScreenshotExportService.GetDefaultFolder();
    }

    private void ChooseScreenshotFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var current = _settingsViewModel.ScreenshotSaveFolder?.Trim();
        if (string.IsNullOrWhiteSpace(current) || !Directory.Exists(current))
        {
            current = ScreenshotExportService.GetDefaultFolder();
        }

        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder to save screenshots",
            SelectedPath = current,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            _settingsViewModel.ScreenshotSaveFolder = dialog.SelectedPath;
        }
    }

    private static string GetProviderTemplate(WebSearchSettings settings, string provider)
    {
        if (settings.Providers.TryGetValue(provider, out var template) && !string.IsNullOrWhiteSpace(template))
        {
            return template;
        }
        return SettingsService.GetDefaultWebSearchTemplate(provider);
    }

    private static bool TryValidateTemplate(string provider, string template, out string error)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            error = $"{provider} template is required.";
            return false;
        }
        if (!template.Contains("{q}", StringComparison.Ordinal))
        {
            error = $"{provider} template must include {{q}}.";
            return false;
        }

        var testUrl = template.Replace("{q}", "test");
        if (!Uri.TryCreate(testUrl, UriKind.Absolute, out var uri))
        {
            error = $"{provider} template must be a valid URL.";
            return false;
        }
        if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            error = $"{provider} template must start with http or https.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void SetWebSearchStatus(string message, bool autoClear)
    {
        _settingsViewModel.WebSearchStatusMessage = message;
        if (!autoClear)
        {
            return;
        }

        var requestId = ++_webSearchStatusRequestId;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(1600);
            await Dispatcher.InvokeAsync(() =>
            {
                if (requestId == _webSearchStatusRequestId)
                {
                    _settingsViewModel.WebSearchStatusMessage = string.Empty;
                }
            });
        });
    }

    private void CloseSettingsModal()
    {
        if (!_settingsViewModel.IsSettingsOpen)
        {
            return;
        }

        _settingsViewModel.IsCapturingHotkey = false;
        _settingsViewModel.IsSettingsOpen = false;

        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
        fade.Completed += (_, _) =>
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
            SettingsOverlay.IsHitTestVisible = false;
        };
        SettingsOverlay.BeginAnimation(OpacityProperty, fade);
        SettingsModalScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.92, TimeSpan.FromMilliseconds(150)));
        SettingsModalScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.92, TimeSpan.FromMilliseconds(150)));
    }

    private void ThemeModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (sender is not System.Windows.Controls.ComboBox combo || combo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        ThemeMode mode;
        if (item.Tag is ThemeMode tagMode)
        {
            mode = tagMode;
        }
        else if (!Enum.TryParse(item.Tag?.ToString(), out mode))
        {
            return;
        }

        _settingsViewModel.ThemeMode = mode;
        (Application.Current as App)?.SetThemeMode(mode);
    }
}
