using System;
using System.Collections.Generic;
using Application = System.Windows.Application;
using System.Windows;
using Microsoft.Win32;
using FrozenOCR.Settings;

namespace FrozenOCR.UI.Themes;

internal static class ThemeManager
{
    private static ThemeMode _overrideMode = ThemeMode.System;
    private static ResourceDictionary? _shared;
    private static ResourceDictionary? _currentTheme;
    private static bool _systemEventsHooked;

    public static void Initialize(ThemeMode overrideMode)
    {
        _overrideMode = overrideMode;
        ApplyTheme(GetEffectiveTheme());
        TryHookSystemEvents();
    }

    public static void Shutdown()
    {
        if (_systemEventsHooked)
        {
            try
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            }
            catch (PlatformNotSupportedException)
            {
                // Ignore if SystemEvents is unavailable in this environment.
            }
            _systemEventsHooked = false;
        }
    }

    public static void SetOverride(ThemeMode mode)
    {
        _overrideMode = mode;
        ApplyTheme(GetEffectiveTheme());
    }

    private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (_overrideMode != ThemeMode.System)
        {
            return;
        }

        ApplyTheme(GetEffectiveTheme());
    }

    private static void TryHookSystemEvents()
    {
        if (_systemEventsHooked)
        {
            return;
        }

        try
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _systemEventsHooked = true;
        }
        catch (PlatformNotSupportedException)
        {
            // SystemEvents isn't available (e.g., non-interactive/unsupported host). Skip.
            _systemEventsHooked = false;
        }
    }

    private static ThemeMode GetEffectiveTheme()
    {
        if (_overrideMode != ThemeMode.System)
        {
            return _overrideMode;
        }

        var isLight = ReadAppsUseLightTheme();
        return isLight ? ThemeMode.Light : ThemeMode.Dark;
    }

    private static bool ReadAppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int intValue)
            {
                return intValue != 0;
            }
        }
        catch
        {
            // ignore and fall back to light
        }

        return true;
    }

    private static void ApplyTheme(ThemeMode mode)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        _shared ??= new ResourceDictionary
        {
            Source = new Uri("UI/Themes/Theme.Shared.xaml", UriKind.Relative)
        };

        var themeSource = mode == ThemeMode.Dark
            ? "UI/Themes/Theme.Dark.xaml"
            : "UI/Themes/Theme.Light.xaml";

        var themeDict = new ResourceDictionary
        {
            Source = new Uri(themeSource, UriKind.Relative)
        };

        var dictionaries = app.Resources.MergedDictionaries;
        dictionaries.Remove(_currentTheme);

        if (!dictionaries.Contains(_shared))
        {
            dictionaries.Add(_shared);
        }

        dictionaries.Add(themeDict);
        _currentTheme = themeDict;
    }
}
