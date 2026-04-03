using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrozenOCR.Core;
using FrozenOCR.Native;
using FrozenOCR.Ocr;

namespace FrozenOCR.Settings;

internal sealed class SettingsService
{
    public static readonly HotkeySettings DefaultHotkey = new(
        Modifiers: NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT,
        Key: 0x20 // VK_SPACE
    );
    public const string ProviderGoogle = "Google";
    public const string ProviderBing = "Bing";
    public const string ProviderDuckDuckGo = "DuckDuckGo";

    public static readonly AppSettings DefaultSettings = CreateDefaultAppSettings();

    private readonly string _path;
    private readonly object _sync = new();
    private AppSettings? _cachedSettings;
    private DateTime _cachedLastWriteUtc;

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FrozenOCR"
        );
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

    public AppSettings Load()
    {
        lock (_sync)
        {
            return LoadCore();
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_sync)
        {
            SaveCore(settings);
        }
    }

    public WebSearchSettings GetWebSearchSettings()
    {
        var settings = Load();
        return NormalizeWebSearch(settings.WebSearch);
    }

    public OcrSettings GetOcrSettings()
    {
        var settings = Load();
        return NormalizeOcrSettings(settings.Ocr);
    }

    public WebTranslateSettings GetWebTranslateSettings()
    {
        var settings = Load();
        return NormalizeWebTranslate(settings.WebTranslate);
    }

    public ScreenshotSettings GetScreenshotSettings()
    {
        var settings = Load();
        return NormalizeScreenshotSettings(settings.Screenshot);
    }

    public void UpdateWebSearchSettings(WebSearchSettings webSearch)
    {
        var normalized = NormalizeWebSearch(webSearch);
        UpdateAppSettings(current => current with { WebSearch = normalized });
    }

    public void UpdateOcrSettings(OcrSettings ocr)
    {
        var normalized = NormalizeOcrSettings(ocr);
        UpdateAppSettings(current => current with { Ocr = normalized });
    }

    public void UpdateWebTranslateSettings(WebTranslateSettings webTranslate)
    {
        var normalized = NormalizeWebTranslate(webTranslate);
        UpdateAppSettings(current => current with { WebTranslate = normalized });
    }

    public void UpdateScreenshotSettings(ScreenshotSettings screenshot)
    {
        var normalized = NormalizeScreenshotSettings(screenshot);
        UpdateAppSettings(current => current with { Screenshot = normalized });
    }

    public static WebSearchSettings CreateDefaultWebSearch()
    {
        return new WebSearchSettings
        {
            DefaultProvider = ProviderGoogle,
            Providers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ProviderGoogle] = "https://www.google.com/search?q={q}",
                [ProviderBing] = "https://www.bing.com/search?q={q}",
                [ProviderDuckDuckGo] = "https://duckduckgo.com/?q={q}"
            }
        };
    }

    public static string GetDefaultWebSearchTemplate(string provider)
    {
        var defaults = CreateDefaultWebSearch();
        return defaults.Providers.TryGetValue(provider, out var template)
            ? template
            : defaults.Providers[ProviderGoogle];
    }

    public static string BuildSearchUrl(WebSearchSettings settings, string? provider, string query)
    {
        var resolvedProvider = string.IsNullOrWhiteSpace(provider)
            ? settings.DefaultProvider
            : provider;

        if (!settings.Providers.TryGetValue(resolvedProvider, out var template)
            || string.IsNullOrWhiteSpace(template))
        {
            template = GetDefaultWebSearchTemplate(resolvedProvider);
        }

        return template.Replace("{q}", Uri.EscapeDataString(query));
    }

    public static WebTranslateSettings CreateDefaultWebTranslate()
    {
        return new WebTranslateSettings
        {
            DefaultProvider = ProviderGoogle,
            From = "auto",
            To = "en",
            Providers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ProviderGoogle] = "https://translate.google.com/?sl={from}&tl={to}&text={q}&op=translate",
                [ProviderBing] = "https://www.bing.com/translator?from={from}&to={to}&text={q}"
            }
        };
    }

    public static ScreenshotSettings CreateDefaultScreenshot()
    {
        return new ScreenshotSettings
        {
            SaveFolder = ScreenshotExportService.GetDefaultFolder()
        };
    }

    public static string GetDefaultWebTranslateTemplate(string provider)
    {
        var defaults = CreateDefaultWebTranslate();
        return defaults.Providers.TryGetValue(provider, out var template)
            ? template
            : defaults.Providers[ProviderGoogle];
    }

    public static string BuildTranslateUrl(
        WebTranslateSettings settings,
        string? provider,
        string? from,
        string? to,
        string query)
    {
        var resolvedProvider = string.IsNullOrWhiteSpace(provider)
            ? settings.DefaultProvider
            : provider;

        if (!settings.Providers.TryGetValue(resolvedProvider, out var template)
            || string.IsNullOrWhiteSpace(template))
        {
            template = GetDefaultWebTranslateTemplate(resolvedProvider);
        }

        var fromCode = string.IsNullOrWhiteSpace(from) ? settings.From : from;
        var toCode = string.IsNullOrWhiteSpace(to) ? settings.To : to;

        return template
            .Replace("{from}", fromCode)
            .Replace("{to}", toCode)
            .Replace("{q}", Uri.EscapeDataString(query));
    }

    public void UpdateAppSettings(Func<AppSettings, AppSettings> updater)
    {
        ArgumentNullException.ThrowIfNull(updater);

        lock (_sync)
        {
            var current = GetCurrentSettingsLocked();
            var updated = updater(current);
            SaveCore(updated);
        }
    }

    private void SaveCore(AppSettings settings)
    {
        var normalized = NormalizeSettings(settings);
        var json = JsonSerializer.Serialize(normalized, GetJsonOptions());
        File.WriteAllText(_path, json);
        _cachedSettings = normalized;
        _cachedLastWriteUtc = File.GetLastWriteTimeUtc(_path);
    }

    private static AppSettings CreateDefaultAppSettings()
    {
        return new AppSettings
        {
            Hotkey = DefaultHotkey,
            EnableMouseChord = true,
            ThemeMode = ThemeMode.System,
            Ocr = new OcrSettings(),
            Screenshot = CreateDefaultScreenshot(),
            WebSearch = CreateDefaultWebSearch(),
            WebTranslate = CreateDefaultWebTranslate(),
            HasSeenTrayHint = false
        };
    }

    private static AppSettings NormalizeSettings(AppSettings settings)
    {
        var normalizedWeb = NormalizeWebSearch(settings.WebSearch);
        var normalizedTranslate = NormalizeWebTranslate(settings.WebTranslate);
        var normalizedOcr = NormalizeOcrSettings(settings.Ocr);
        var normalizedScreenshot = NormalizeScreenshotSettings(settings.Screenshot);
        var normalizedTheme = settings.ThemeMode;
        if (!Enum.IsDefined(typeof(ThemeMode), normalizedTheme))
        {
            normalizedTheme = ThemeMode.System;
        }
        return settings with
        {
            WebSearch = normalizedWeb,
            WebTranslate = normalizedTranslate,
            Ocr = normalizedOcr,
            Screenshot = normalizedScreenshot,
            ThemeMode = normalizedTheme,
            HasSeenTrayHint = settings.HasSeenTrayHint
        };
    }

    private static WebSearchSettings NormalizeWebSearch(WebSearchSettings? webSearch)
    {
        var defaults = CreateDefaultWebSearch();
        var providers = webSearch?.Providers is not null
            ? new Dictionary<string, string>(webSearch.Providers, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in defaults.Providers)
        {
            if (!providers.TryGetValue(pair.Key, out var template) || string.IsNullOrWhiteSpace(template))
            {
                providers[pair.Key] = pair.Value;
            }
        }

        var defaultProvider = webSearch?.DefaultProvider;
        if (string.IsNullOrWhiteSpace(defaultProvider) || !providers.ContainsKey(defaultProvider))
        {
            defaultProvider = defaults.DefaultProvider;
        }

        return new WebSearchSettings
        {
            DefaultProvider = defaultProvider,
            Providers = providers
        };
    }

    private static WebTranslateSettings NormalizeWebTranslate(WebTranslateSettings? webTranslate)
    {
        var defaults = CreateDefaultWebTranslate();
        var providers = webTranslate?.Providers is not null
            ? new Dictionary<string, string>(webTranslate.Providers, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in defaults.Providers)
        {
            if (!providers.TryGetValue(pair.Key, out var template) || string.IsNullOrWhiteSpace(template))
            {
                providers[pair.Key] = pair.Value;
            }
        }

        var defaultProvider = webTranslate?.DefaultProvider;
        if (string.IsNullOrWhiteSpace(defaultProvider))
        {
            defaultProvider = defaults.DefaultProvider;
        }
        else if (!providers.ContainsKey(defaultProvider))
        {
            defaultProvider = defaults.DefaultProvider;
        }

        var from = string.IsNullOrWhiteSpace(webTranslate?.From) ? defaults.From : webTranslate!.From!;
        var to = string.IsNullOrWhiteSpace(webTranslate?.To) ? defaults.To : webTranslate!.To!;

        return new WebTranslateSettings
        {
            DefaultProvider = defaultProvider,
            From = from,
            To = to,
            Providers = providers
        };
    }

    private static ScreenshotSettings NormalizeScreenshotSettings(ScreenshotSettings? screenshot)
    {
        var defaults = CreateDefaultScreenshot();
        var folder = screenshot?.SaveFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            folder = defaults.SaveFolder;
        }
        return new ScreenshotSettings
        {
            SaveFolder = folder
        };
    }

    private static OcrSettings NormalizeOcrSettings(OcrSettings? ocr)
    {
        var preferred = string.IsNullOrWhiteSpace(ocr?.LanguageTag)
            ? OcrSettings.Auto
            : ocr!.LanguageTag!;
        var mode = ocr?.LanguageMode ?? LanguageMode.Auto;
        var provider = ocr?.ProviderMode ?? OcrProviderMode.Auto;
        var profile = ocr?.PreprocessProfile ?? PreprocessProfile.Auto;
        var recognitionMode = ocr?.RecognitionMode ?? RecognitionMode.Default;
        return new OcrSettings
        {
            LanguageMode = mode,
            LanguageTag = preferred,
            ProviderMode = provider,
            PreprocessProfile = profile,
            RecognitionMode = recognitionMode
        };
    }
    private static JsonSerializerOptions GetJsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    private AppSettings GetCurrentSettingsLocked()
    {
        return _cachedSettings is not null
            ? CloneSettings(_cachedSettings)
            : LoadCore();
    }

    private AppSettings LoadCore()
    {
        try
        {
            if (!File.Exists(_path))
            {
                _cachedSettings = CloneSettings(DefaultSettings);
                _cachedLastWriteUtc = DateTime.MinValue;
                return CloneSettings(_cachedSettings);
            }

            var lastWriteUtc = File.GetLastWriteTimeUtc(_path);
            if (_cachedSettings is not null && lastWriteUtc == _cachedLastWriteUtc)
            {
                return CloneSettings(_cachedSettings);
            }

            var json = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json))
            {
                _cachedSettings = CloneSettings(DefaultSettings);
                _cachedLastWriteUtc = lastWriteUtc;
                return CloneSettings(_cachedSettings);
            }

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("Hotkey", out _))
            {
                var settings = JsonSerializer.Deserialize<AppSettings?>(json, GetJsonOptions());
                if (settings is not null)
                {
                    _cachedSettings = NormalizeSettings(settings);
                    _cachedLastWriteUtc = lastWriteUtc;
                    return CloneSettings(_cachedSettings);
                }
            }
            else
            {
                // Backward compatibility: old format with just Modifiers/Key.
                var legacy = JsonSerializer.Deserialize<HotkeySettings?>(json, GetJsonOptions());
                if (legacy is not null)
                {
                    var migrated = CreateDefaultAppSettings() with
                    {
                        Hotkey = legacy.Value
                    };
                    SaveCore(migrated);
                    return CloneSettings(_cachedSettings!);
                }
            }

            _cachedSettings = CloneSettings(DefaultSettings);
            _cachedLastWriteUtc = lastWriteUtc;
            return CloneSettings(_cachedSettings);
        }
        catch
        {
            _cachedSettings = CloneSettings(DefaultSettings);
            _cachedLastWriteUtc = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
            return CloneSettings(_cachedSettings);
        }
    }

    private static AppSettings CloneSettings(AppSettings settings) => NormalizeSettings(settings);
}

internal readonly record struct HotkeySettings(uint Modifiers, uint Key);

internal enum ThemeMode
{
    System,
    Dark,
    Light
}

internal sealed record AppSettings
{
    public HotkeySettings Hotkey { get; init; } = SettingsService.DefaultHotkey;
    public bool EnableMouseChord { get; init; } = true;
    public ThemeMode ThemeMode { get; init; } = ThemeMode.System;
    public OcrSettings Ocr { get; init; } = new();
    public ScreenshotSettings Screenshot { get; init; } = SettingsService.CreateDefaultScreenshot();
    public WebSearchSettings WebSearch { get; init; } = SettingsService.CreateDefaultWebSearch();
    public WebTranslateSettings WebTranslate { get; init; } = SettingsService.CreateDefaultWebTranslate();
    public bool HasSeenTrayHint { get; init; }
}

internal sealed record WebSearchSettings
{
    public string DefaultProvider { get; init; } = SettingsService.ProviderGoogle;
    public Dictionary<string, string> Providers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed record WebTranslateSettings
{
    public string DefaultProvider { get; init; } = SettingsService.ProviderGoogle;
    public string From { get; init; } = "auto";
    public string To { get; init; } = "en";
    public Dictionary<string, string> Providers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed record ScreenshotSettings
{
    public string SaveFolder { get; init; } = ScreenshotExportService.GetDefaultFolder();
}


internal sealed record OcrSettings
{
    public const string Auto = "auto";
    public OcrProviderMode ProviderMode { get; init; } = OcrProviderMode.Auto;
    public LanguageMode LanguageMode { get; init; } = LanguageMode.Auto;
    public string LanguageTag { get; init; } = Auto;
    public PreprocessProfile PreprocessProfile { get; init; } = PreprocessProfile.Auto;
    public RecognitionMode RecognitionMode { get; init; } = RecognitionMode.Default;
}
