using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MessageBox = System.Windows.MessageBox;
using FrozenOCR.Core;
using FrozenOCR.Settings;

namespace FrozenOCR;

internal partial class TraySettingsWindow : Window
{
    private readonly App _app;
    private readonly SettingsService _settingsService;
    private bool _isLoading;

    public TraySettingsWindow(App app, SettingsService settingsService)
    {
        InitializeComponent();
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        BrowseFolderButton.Content = "Browse...";
        BrowseFolderButton.Margin = new Thickness(0, 0, 8, 0);

        TrySetWindowIcon();

        ThemeCombo.Items.Add("System");
        ThemeCombo.Items.Add("Light");
        ThemeCombo.Items.Add("Dark");
        ThemeCombo.SelectedIndex = 0;

        _isLoading = true;
        try
        {
            LoadCurrent();
        }
        finally
        {
            _isLoading = false;
        }
    }

    internal void ReloadFromAppState()
    {
        _isLoading = true;
        try
        {
            LoadCurrent();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void TrySetWindowIcon()
    {
        try
        {
            var path = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            using var ico = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (ico == null) return;
            var bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(ico.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bs.Freeze();
            Icon = bs;
        }
        catch
        {
            // ignore
        }
    }

    private void LoadCurrent()
    {
        HotkeyBox.Text = _app.GetCurrentHotkeyDisplay();
        ScreenshotFolderBox.Text = _settingsService.GetScreenshotSettings().SaveFolder ?? string.Empty;
        if (MouseChordCheckBox != null)
            MouseChordCheckBox.IsChecked = _app.GetEnableMouseChord();

        var mode = _app.GetThemeMode();
        if (ThemeCombo != null)
        {
            ThemeCombo.SelectedIndex = mode switch
            {
                ThemeMode.System => 0,
                ThemeMode.Light => 1,
                ThemeMode.Dark => 2,
                _ => 0
            };
        }
    }

    private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder to save screenshots",
            SelectedPath = string.IsNullOrWhiteSpace(ScreenshotFolderBox.Text)
                ? ScreenshotExportService.GetDefaultFolder()
                : ScreenshotFolderBox.Text
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            ScreenshotFolderBox.Text = dialog.SelectedPath;
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = GetSelectedFolder();
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open the folder.\r\n\r\n{ex.Message}", "FrozenOCR", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || _app is null || ThemeCombo == null || ThemeCombo.SelectedIndex < 0) return;
        var mode = ThemeCombo.SelectedIndex switch
        {
            0 => ThemeMode.System,
            1 => ThemeMode.Light,
            2 => ThemeMode.Dark,
            _ => ThemeMode.System
        };
        _app.SetThemeMode(mode);
    }

    private void MouseChordCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _app is null) return;
        _app.SetEnableMouseChord(MouseChordCheckBox?.IsChecked == true);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = GetSelectedFolder();
        if (!Directory.Exists(folder))
        {
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch
            {
                MessageBox.Show("Could not create the folder.", "FrozenOCR", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        _settingsService.UpdateScreenshotSettings(new ScreenshotSettings { SaveFolder = folder });
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private string GetSelectedFolder()
    {
        var folder = ScreenshotFolderBox.Text?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(folder)
            ? ScreenshotExportService.GetDefaultFolder()
            : folder;
    }
}
