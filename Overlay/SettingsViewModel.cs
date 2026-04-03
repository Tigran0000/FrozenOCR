using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FrozenOCR.Overlay;

internal sealed class SettingsViewModel : INotifyPropertyChanged
{
    private bool _isSettingsOpen;
    private bool _isCapturingHotkey;
    private string _currentHotkeyDisplay = "Ctrl + Alt + Space";
    private string _statusMessage = string.Empty;
    private bool _enableMouseChord = true;
    private Settings.ThemeMode _themeMode = Settings.ThemeMode.System;
    private ObservableCollection<OcrLanguageOption> _ocrLanguages = new();
    private Ocr.OcrProviderMode _ocrProviderMode = Ocr.OcrProviderMode.Auto;
    private Ocr.LanguageMode _ocrLanguageMode = Ocr.LanguageMode.Auto;
    private string _ocrLanguageTag = Settings.OcrSettings.Auto;
    private string _ocrStatusMessage = string.Empty;
    private string _webSearchDefaultProvider = "Google";
    private bool _webSearchAdvancedOpen;
    private string _googleTemplate = string.Empty;
    private string _bingTemplate = string.Empty;
    private string _duckDuckGoTemplate = string.Empty;
    private string _webSearchStatusMessage = string.Empty;
    private string _screenshotSaveFolder = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => SetField(ref _isSettingsOpen, value);
    }

    public bool IsCapturingHotkey
    {
        get => _isCapturingHotkey;
        set => SetField(ref _isCapturingHotkey, value);
    }

    public string CurrentHotkeyDisplay
    {
        get => _currentHotkeyDisplay;
        set => SetField(ref _currentHotkeyDisplay, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public bool EnableMouseChord
    {
        get => _enableMouseChord;
        set => SetField(ref _enableMouseChord, value);
    }

    public Settings.ThemeMode ThemeMode
    {
        get => _themeMode;
        set => SetField(ref _themeMode, value);
    }

    public ObservableCollection<OcrLanguageOption> OcrLanguages
    {
        get => _ocrLanguages;
        set => SetField(ref _ocrLanguages, value);
    }

    public Ocr.OcrProviderMode OcrProviderMode
    {
        get => _ocrProviderMode;
        set => SetField(ref _ocrProviderMode, value);
    }

    public Ocr.LanguageMode OcrLanguageMode
    {
        get => _ocrLanguageMode;
        set => SetField(ref _ocrLanguageMode, value);
    }

    public string OcrLanguageTag
    {
        get => _ocrLanguageTag;
        set => SetField(ref _ocrLanguageTag, value);
    }

    public string OcrStatusMessage
    {
        get => _ocrStatusMessage;
        set => SetField(ref _ocrStatusMessage, value);
    }

    public string WebSearchDefaultProvider
    {
        get => _webSearchDefaultProvider;
        set => SetField(ref _webSearchDefaultProvider, value);
    }

    public bool WebSearchAdvancedOpen
    {
        get => _webSearchAdvancedOpen;
        set => SetField(ref _webSearchAdvancedOpen, value);
    }

    public string GoogleTemplate
    {
        get => _googleTemplate;
        set => SetField(ref _googleTemplate, value);
    }

    public string BingTemplate
    {
        get => _bingTemplate;
        set => SetField(ref _bingTemplate, value);
    }

    public string DuckDuckGoTemplate
    {
        get => _duckDuckGoTemplate;
        set => SetField(ref _duckDuckGoTemplate, value);
    }

    public string WebSearchStatusMessage
    {
        get => _webSearchStatusMessage;
        set => SetField(ref _webSearchStatusMessage, value);
    }

    public string ScreenshotSaveFolder
    {
        get => _screenshotSaveFolder;
        set => SetField(ref _screenshotSaveFolder, value);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

internal readonly record struct OcrLanguageOption(string Tag, string DisplayName);
