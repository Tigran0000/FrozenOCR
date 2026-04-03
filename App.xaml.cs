using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using FrozenOCR.Capture;
using FrozenOCR.Core;
using FrozenOCR.Display;
using FrozenOCR.Input;
using FrozenOCR.Native;
using FrozenOCR.Settings;
using FrozenOCR.UI.Themes;

namespace FrozenOCR;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private GlobalHotkeyService? _hotkey;
    private MouseChordLauncherService? _mouseChord;
    private OcrFlowController? _controller;
    private SingleInstanceGuard? _singleInstance;
    private SettingsService? _settingsService;
    private NotifyIcon? _trayIcon;
    private TraySettingsWindow? _traySettingsWindow;
    private readonly object _controllerLock = new();
    private bool _enableMouseChord = true;
    private ThemeMode _themeMode = ThemeMode.System;
    private int _fatalErrorDialogShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Required for Windows App SDK when published as single-file (extraction base path).
        Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);

        base.OnStartup(e);

        try
        {
            var exePath = Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
            var ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            var pid = Environment.ProcessId;
            Log.Info($"App starting | pid={pid} | version={ver} | exe=\"{exePath}\"");

            _singleInstance = new SingleInstanceGuard(@"Local\FrozenOCR.SingleInstance");
            if (!_singleInstance.HasHandle)
            {
                MessageBox.Show(
                    "FrozenOCR is already running.\r\n\r\nUse Ctrl+Alt+Space to trigger it, or end the existing FrozenOCR task to restart.",
                    "FrozenOCR",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                Shutdown(0);
                return;
            }

            DispatcherUnhandledException += (_, ex) =>
            {
                HandleUnhandledException("DispatcherUnhandledException", ex.Exception, showDialog: true);
                ex.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            {
                if (ex.ExceptionObject is Exception exception)
                {
                    HandleUnhandledException("UnhandledException", exception, showDialog: false);
                    return;
                }

                Log.Error($"UnhandledException: {ex.ExceptionObject}");
            };

            _settingsService = new SettingsService();
            var settings = _settingsService.Load();
            var hotkeySettings = settings.Hotkey;
            _enableMouseChord = settings.EnableMouseChord;
            _themeMode = settings.ThemeMode;
            var modifiers = hotkeySettings.Modifiers | NativeMethods.MOD_NOREPEAT;
            var virtualKey = hotkeySettings.Key;

            ThemeManager.Initialize(_themeMode);

            try
            {
                _hotkey = new GlobalHotkeyService(
                    hotkeyId: 1,
                    modifiers: modifiers,
                    virtualKey: virtualKey
                );
            }
            catch (Win32Exception)
            {
                var fallback = SettingsService.DefaultHotkey;
                _hotkey = new GlobalHotkeyService(
                    hotkeyId: 1,
                    modifiers: fallback.Modifiers | NativeMethods.MOD_NOREPEAT,
                    virtualKey: fallback.Key
                );
                var current = _settingsService.Load();
                _settingsService.Save(current with
                {
                    Hotkey = fallback,
                    EnableMouseChord = _enableMouseChord,
                    ThemeMode = _themeMode
                });
            }

            Log.Info($"Hotkey registered {GetCurrentHotkeyDisplay()}");
            Log.Memory("Startup");
            _hotkey.HotkeyPressed += (_, _) => _ = GetOrCreateController().TryStartAsync();

            _mouseChord = new MouseChordLauncherService();
            _mouseChord.ChordActivated += (_, point) =>
            {
                if (!_enableMouseChord) return;
                var ctrl = GetOrCreateController();
                if (ctrl.IsRunning) return;
                _ = ctrl.TryStartForPointAsync(point);
            };

            SetupTrayIcon();
            MaybeShowFirstRunTrayHint();
        }
        catch (Win32Exception ex)
        {
            Log.Error($"Win32Exception during startup: {ex}");
            MessageBox.Show(
                $"FrozenOCR couldn't start because a Windows system call failed.\r\n\r\n{ex.Message}\r\n\r\nMost common cause: Ctrl+Alt+Space is already used by another app.\r\nClose the conflicting app or choose a different hotkey (we can add hotkey settings next).",
                "FrozenOCR",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            Shutdown(-1);
        }
        catch (Exception ex)
        {
            Log.Error($"Exception during startup: {ex}");
            MessageBox.Show(
                $"FrozenOCR couldn't start:\r\n{ex}",
                "FrozenOCR",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_traySettingsWindow is not null)
        {
            _traySettingsWindow.Close();
            _traySettingsWindow = null;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
        }
        _trayIcon?.Dispose();
        _trayIcon = null;
        _hotkey?.Dispose();
        _mouseChord?.Dispose();
        _singleInstance?.Dispose();
        ThemeManager.Shutdown();
        Log.Info("App exiting");
        base.OnExit(e);
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Visible = true
        };
        RefreshTrayIconText();

        try
        {
            var exePath = Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                using var ico = Icon.ExtractAssociatedIcon(exePath);
                if (ico != null)
                {
                    _trayIcon.Icon = (Icon)ico.Clone();
                }
            }
        }
        catch
        {
            // ignore
        }

        if (_trayIcon.Icon == null)
        {
            _trayIcon.Icon = SystemIcons.Application;
        }

        var openSettings = new ToolStripMenuItem("Open settings");
        openSettings.Click += (_, _) => RunTrayAction(OpenTraySettings, "open settings");

        var openScreenshotFolder = new ToolStripMenuItem("Open screenshot folder");
        openScreenshotFolder.Click += (_, _) => RunTrayAction(OpenScreenshotFolder, "open screenshot folder");

        var close = new ToolStripMenuItem("Close");
        close.Click += (_, _) => RunTrayAction(() => Current.Shutdown(), "close the app");

        _trayIcon.ContextMenuStrip = new ContextMenuStrip();
        _trayIcon.ContextMenuStrip.Items.Add(openSettings);
        _trayIcon.ContextMenuStrip.Items.Add(openScreenshotFolder);
        _trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _trayIcon.ContextMenuStrip.Items.Add(close);

        _trayIcon.DoubleClick += (_, _) => RunTrayAction(OpenTraySettings, "open settings");
    }

    private void RefreshTrayIconText()
    {
        if (_trayIcon is null) return;
        var hotkey = GetCurrentHotkeyDisplay();
        var newText = $"FrozenOCR - {hotkey} to capture";
        void Update()
        {
            if (_trayIcon is null) return;
            _trayIcon.Text = string.Empty;
            _trayIcon.Text = newText;
        }
        if (Dispatcher.CheckAccess())
            Update();
        else
            Dispatcher.BeginInvoke(Update);
    }

    private OcrFlowController GetOrCreateController()
    {
        lock (_controllerLock)
        {
            if (_controller is not null)
                return _controller;
            var monitorService = new MonitorService();
            var captureService = new ScreenCaptureService();
            var settingsService = _settingsService ?? new SettingsService();
            _settingsService ??= settingsService;
            _controller = new OcrFlowController(monitorService, captureService, settingsService);
            return _controller;
        }
    }

    private void OpenTraySettings()
    {
        if (_settingsService == null) return;

        if (_traySettingsWindow is not null)
        {
            _traySettingsWindow.ReloadFromAppState();
            if (_traySettingsWindow.WindowState == WindowState.Minimized)
            {
                _traySettingsWindow.WindowState = WindowState.Normal;
            }
            _traySettingsWindow.Show();
            _traySettingsWindow.Activate();
            _traySettingsWindow.Focus();
            return;
        }

        var window = new TraySettingsWindow(this, _settingsService);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_traySettingsWindow, window))
            {
                _traySettingsWindow = null;
            }
        };

        _traySettingsWindow = window;
        window.Show();
        window.Activate();
        window.Focus();
        Log.Info("Tray settings window opened");
    }

    private void OpenScreenshotFolder()
    {
        try
        {
            var folder = _settingsService?.GetScreenshotSettings().SaveFolder
                ?? ScreenshotExportService.GetDefaultFolder();
            if (string.IsNullOrWhiteSpace(folder))
            {
                folder = ScreenshotExportService.GetDefaultFolder();
            }
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            Log.Info($"Opened screenshot folder \"{folder}\"");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to open screenshot folder: {ex}");
            MessageBox.Show($"Could not open folder: {ex.Message}", "FrozenOCR", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    internal string GetCurrentHotkeyDisplay()
    {
        if (_hotkey is null)
        {
            return "Ctrl + Alt + Space";
        }

        var mods = _hotkey.Modifiers & ~NativeMethods.MOD_NOREPEAT;
        return HotkeyCaptureHelper.FormatHotkey(mods, _hotkey.VirtualKey);
    }

    internal uint GetHotkeyModifiers()
    {
        return _hotkey is null
            ? (NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT)
            : _hotkey.Modifiers & ~NativeMethods.MOD_NOREPEAT;
    }

    internal uint GetHotkeyKey()
    {
        return _hotkey?.VirtualKey ?? 0x20;
    }

    internal bool TryUpdateHotkey(uint modifiers, uint virtualKey, out string? error)
    {
        error = null;
        if (_hotkey is null || _settingsService is null)
        {
            error = "Hotkey service not ready.";
            return false;
        }

        var updated = _hotkey.TryUpdateHotkey(modifiers | NativeMethods.MOD_NOREPEAT, virtualKey, out var updateError);
        if (!updated)
        {
            error = updateError ?? "Failed to register hotkey.";
            return false;
        }

        PersistAppSettings(current => current with
        {
            Hotkey = new HotkeySettings(modifiers, virtualKey),
            EnableMouseChord = _enableMouseChord,
            ThemeMode = _themeMode
        });
        RefreshTrayIconText();
        _traySettingsWindow?.ReloadFromAppState();
        Log.Info($"Hotkey updated to {HotkeyCaptureHelper.FormatHotkey(modifiers, virtualKey)}");
        return true;
    }

    internal bool GetEnableMouseChord() => _enableMouseChord;

    internal void SetEnableMouseChord(bool enabled)
    {
        _enableMouseChord = enabled;
        PersistCurrentAppState();
        _traySettingsWindow?.ReloadFromAppState();
        Log.Info($"Mouse chord enabled={_enableMouseChord}");
    }

    internal ThemeMode GetThemeMode() => _themeMode;

    internal void SetThemeMode(ThemeMode mode)
    {
        _themeMode = mode;
        ThemeManager.SetOverride(mode);
        PersistCurrentAppState();
        _traySettingsWindow?.ReloadFromAppState();
        Log.Info($"Theme mode set to {_themeMode}");
    }

    private void PersistCurrentAppState()
    {
        var hotkey = new HotkeySettings(GetHotkeyModifiers(), GetHotkeyKey());
        PersistAppSettings(current => current with
        {
            Hotkey = hotkey,
            EnableMouseChord = _enableMouseChord,
            ThemeMode = _themeMode
        });
    }

    private void PersistAppSettings(Func<AppSettings, AppSettings> updater)
    {
        if (_settingsService is null)
        {
            return;
        }

        _settingsService.UpdateAppSettings(updater);
    }

    private void MaybeShowFirstRunTrayHint()
    {
        if (_trayIcon is null || _settingsService is null)
        {
            return;
        }

        var settings = _settingsService.Load();
        if (settings.HasSeenTrayHint)
        {
            return;
        }

        try
        {
            _trayIcon.BalloonTipTitle = "FrozenOCR is running";
            _trayIcon.BalloonTipText = $"{GetCurrentHotkeyDisplay()} captures the monitor under your cursor. Use the tray icon for settings.";
            _trayIcon.ShowBalloonTip(4000);
            PersistAppSettings(current => current with { HasSeenTrayHint = true });
            Log.Info("Displayed first-run tray hint");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to show tray hint: {ex}");
        }
    }

    private void RunTrayAction(Action action, string operation)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error($"Tray action failed ({operation}): {ex}");
            MessageBox.Show(
                $"FrozenOCR couldn't {operation}.\r\n\r\n{ex.Message}",
                "FrozenOCR",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void HandleUnhandledException(string source, Exception? exception, bool showDialog)
    {
        Log.Error($"{source}: {exception}");
        if (!showDialog || exception is null)
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            ShowFatalErrorDialogOnce(exception);
            return;
        }

        Dispatcher.BeginInvoke(() => ShowFatalErrorDialogOnce(exception), DispatcherPriority.Send);
    }

    private void ShowFatalErrorDialogOnce(Exception exception)
    {
        if (System.Threading.Interlocked.Exchange(ref _fatalErrorDialogShown, 1) == 1)
        {
            return;
        }

        MessageBox.Show(
            $"Unhandled error:\r\n{exception}",
            "FrozenOCR",
            MessageBoxButton.OK,
            MessageBoxImage.Error
        );
    }
}

