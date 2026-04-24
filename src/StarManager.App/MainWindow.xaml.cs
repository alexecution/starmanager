using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using StarManager.App.Models;
using StarManager.App.Services;

namespace StarManager.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly StarDiscoveryService _discoveryService = new();
    private readonly ProviderProcessService _providerProcessService = new();
    private readonly CoagulatorProcessService _coagulatorProcessService = new();
    private readonly SettingsService _settingsService = new();

    private string? _selectedStarRoot;
    private StarScanResult? _scanResult;
    private AppSettings _settings = new();
    private bool _isDarkThemeActive;

    public ObservableCollection<ProviderItem> Providers { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        SourceInitialized += OnSourceInitialized;
        LoadSettings();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplyWindowChromeTheme(_isDarkThemeActive);
    }

    private void BrowseButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select your STAR root folder",
        };

        var result = dialog.ShowDialog(this);
        if (result != true)
        {
            return;
        }

        _selectedStarRoot = dialog.FolderName;
        StarPathTextBox.Text = _selectedStarRoot;
        _settings.LastStarRootPath = _selectedStarRoot;
        _settingsService.Save(_settings);
        StatusTextBlock.Text = "STAR folder selected. Click Scan to detect components.";
    }

    private void ScanButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedStarRoot))
        {
            StatusTextBlock.Text = "Select a STAR folder first.";
            return;
        }

        try
        {
            _scanResult = _discoveryService.Scan(_selectedStarRoot);
            Providers.Clear();

            foreach (var provider in _scanResult.Providers)
            {
                provider.StatusText = _providerProcessService.IsRunning(provider) ? "Running" : "Stopped";
                Providers.Add(provider);
            }

            StatusTextBlock.Text =
                $"Scan complete: {_scanResult.Providers.Count} provider(s) found. " +
                $"STAR app: {(string.IsNullOrWhiteSpace(_scanResult.StarAppEntryPath) ? "missing" : "found")}. " +
                $"Coagulator: {(string.IsNullOrWhiteSpace(_scanResult.CoagulatorEntryPath) ? "missing" : "found")}.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Scan failed: {ex.Message}";
        }
    }

    private void ConfigureProviderButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetProviderFromButton(sender, out var provider))
        {
            return;
        }

        try
        {
            _providerProcessService.LaunchConfigure(provider);
            StatusTextBlock.Text = $"Opened configure UI for provider '{provider.Name}'.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Could not configure '{provider.Name}': {ex.Message}";
        }
    }

    private void StartProviderButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetProviderFromButton(sender, out var provider))
        {
            return;
        }

        try
        {
            _providerProcessService.StartProvider(provider);
            provider.StatusText = "Running";
            StatusTextBlock.Text = $"Started provider '{provider.Name}'.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Could not start '{provider.Name}': {ex.Message}";
        }
    }

    private async void StopProviderButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetProviderFromButton(sender, out var provider))
        {
            return;
        }

        try
        {
            await _providerProcessService.StopProviderAsync(provider);
            provider.StatusText = "Stopped";
            StatusTextBlock.Text = $"Stopped provider '{provider.Name}'.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Could not stop '{provider.Name}': {ex.Message}";
        }
    }

    private void LaunchStarAppButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_scanResult is null || string.IsNullOrWhiteSpace(_scanResult.StarAppEntryPath))
        {
            StatusTextBlock.Text = "STAR app entrypoint was not detected in this setup.";
            return;
        }

        try
        {
            var startInfo = BuildProcessStartInfo(_scanResult.StarAppEntryPath);
            _ = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not launch STAR app process.");

            StatusTextBlock.Text = "STAR app launched.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Could not launch STAR app: {ex.Message}";
        }
    }

    private void OpenCoagulatorWebsiteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_scanResult is null || string.IsNullOrWhiteSpace(_scanResult.CoagulatorWebsiteUrl))
        {
            StatusTextBlock.Text = "No coagulator website URL was detected from configuration.";
            return;
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = _scanResult.CoagulatorWebsiteUrl,
                UseShellExecute = true,
            });

            StatusTextBlock.Text = "Opened coagulator website.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Could not open website: {ex.Message}";
        }
    }

    private void ThemeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is not ComboBoxItem selected)
        {
            return;
        }

        var themeName = selected.Content?.ToString() ?? "System";
        ApplyTheme(themeName);
        _settings.ThemeName = themeName;
        _settingsService.Save(_settings);
    }

    private void StartCoagulatorButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_scanResult is null || string.IsNullOrWhiteSpace(_scanResult.CoagulatorEntryPath))
        {
            StatusTextBlock.Text = "Coagulator entrypoint was not detected in this setup.";
            return;
        }

        try
        {
            _coagulatorProcessService.Start(_scanResult.CoagulatorEntryPath);
            UpdateCoagulatorStatusText();
            StatusTextBlock.Text = "Coagulator started.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Could not start coagulator: {ex.Message}";
        }
    }

    private async void StopCoagulatorButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _coagulatorProcessService.StopAsync();
            UpdateCoagulatorStatusText();
            StatusTextBlock.Text = "Coagulator stopped.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Could not stop coagulator: {ex.Message}";
        }
    }

    private bool TryGetProviderFromButton(object sender, out ProviderItem provider)
    {
        provider = null!;

        if (sender is not Button { Tag: ProviderItem taggedProvider })
        {
            return false;
        }

        provider = taggedProvider;
        return true;
    }

    private static ProcessStartInfo BuildProcessStartInfo(string entryPath)
    {
        var directory = Path.GetDirectoryName(entryPath)
            ?? throw new InvalidOperationException("The entrypoint path has no parent directory.");

        if (Path.GetExtension(entryPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessStartInfo
            {
                FileName = entryPath,
                WorkingDirectory = directory,
                UseShellExecute = true,
            };
        }

        return new ProcessStartInfo
        {
            FileName = "pythonw",
            WorkingDirectory = directory,
            UseShellExecute = true,
            Arguments = $"\"{entryPath}\"",
        };
    }

    private void ApplyTheme(string themeName)
    {
        var useDarkTheme = themeName.Equals("Dark", StringComparison.OrdinalIgnoreCase)
            || (themeName.Equals("System", StringComparison.OrdinalIgnoreCase)
                && IsSystemInDarkMode());

        _isDarkThemeActive = useDarkTheme;
        ApplyThemeResources(useDarkTheme);
        ApplyWindowChromeTheme(useDarkTheme);
    }

    private static void ApplyThemeResources(bool useDarkTheme)
    {
        if (useDarkTheme)
        {
            SetThemeBrush("AppWindowBackgroundBrush", Color.FromRgb(26, 28, 32));
            SetThemeBrush("AppSurfaceBrush", Color.FromRgb(34, 38, 44));
            SetThemeBrush("AppControlBackgroundBrush", Color.FromRgb(42, 46, 54));
            SetThemeBrush("AppControlForegroundBrush", Color.FromRgb(230, 233, 238));
            SetThemeBrush("AppSecondaryForegroundBrush", Color.FromRgb(185, 192, 203));
            SetThemeBrush("AppBorderBrush", Color.FromRgb(78, 88, 102));
            SetThemeBrush("AppAccentBrush", Color.FromRgb(123, 171, 255));
            SetThemeBrush("AppDataGridAltRowBrush", Color.FromRgb(38, 43, 50));
            SetThemeBrush("AppSelectionBrush", Color.FromRgb(64, 88, 130));
            return;
        }

        SetThemeBrush("AppWindowBackgroundBrush", Color.FromRgb(248, 249, 251));
        SetThemeBrush("AppSurfaceBrush", Color.FromRgb(255, 255, 255));
        SetThemeBrush("AppControlBackgroundBrush", Color.FromRgb(255, 255, 255));
        SetThemeBrush("AppControlForegroundBrush", Color.FromRgb(23, 28, 34));
        SetThemeBrush("AppSecondaryForegroundBrush", Color.FromRgb(54, 66, 79));
        SetThemeBrush("AppBorderBrush", Color.FromRgb(216, 221, 229));
        SetThemeBrush("AppAccentBrush", Color.FromRgb(46, 95, 181));
        SetThemeBrush("AppDataGridAltRowBrush", Color.FromRgb(243, 246, 250));
        SetThemeBrush("AppSelectionBrush", Color.FromRgb(204, 224, 255));
    }

    private static void SetThemeBrush(string key, Color color)
    {
        Application.Current.Resources[key] = new SolidColorBrush(color);
    }

    private void ApplyWindowChromeTheme(bool useDarkTheme)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var windowHandle = new WindowInteropHelper(this).Handle;
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        var darkModeFlag = useDarkTheme ? 1 : 0;

        var result = DwmSetWindowAttribute(windowHandle, DwmUseImmersiveDarkMode, ref darkModeFlag, sizeof(int));
        if (result != 0)
        {
            _ = DwmSetWindowAttribute(windowHandle, DwmUseImmersiveDarkModeBefore20H1, ref darkModeFlag, sizeof(int));
        }

        if (useDarkTheme)
        {
            var darkCaptionColor = ToColorRef(Color.FromRgb(26, 28, 32));
            var darkBorderColor = ToColorRef(Color.FromRgb(26, 28, 32));
            var lightTextColor = ToColorRef(Color.FromRgb(230, 233, 238));

            _ = DwmSetWindowAttribute(windowHandle, DwmCaptionColor, ref darkCaptionColor, sizeof(uint));
            _ = DwmSetWindowAttribute(windowHandle, DwmBorderColor, ref darkBorderColor, sizeof(uint));
            _ = DwmSetWindowAttribute(windowHandle, DwmTextColor, ref lightTextColor, sizeof(uint));
            return;
        }

        var defaultColor = DwmColorDefault;
        _ = DwmSetWindowAttribute(windowHandle, DwmCaptionColor, ref defaultColor, sizeof(uint));
        _ = DwmSetWindowAttribute(windowHandle, DwmBorderColor, ref defaultColor, sizeof(uint));
        _ = DwmSetWindowAttribute(windowHandle, DwmTextColor, ref defaultColor, sizeof(uint));
    }

    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;
    private const uint DwmColorDefault = 0xFFFFFFFF;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref uint pvAttribute, int cbAttribute);

    private static uint ToColorRef(Color color)
    {
        return (uint)(color.R | (color.G << 8) | (color.B << 16));
    }

    private void LoadSettings()
    {
        _settings = _settingsService.Load();

        if (!string.IsNullOrWhiteSpace(_settings.LastStarRootPath) && Directory.Exists(_settings.LastStarRootPath))
        {
            _selectedStarRoot = _settings.LastStarRootPath;
            StarPathTextBox.Text = _selectedStarRoot;
            StatusTextBlock.Text = "Loaded last STAR folder from settings. Click Scan to refresh components.";
        }

        var themeName = string.IsNullOrWhiteSpace(_settings.ThemeName) ? "System" : _settings.ThemeName;
        SetThemeSelection(themeName);
        ApplyTheme(themeName);
        UpdateCoagulatorStatusText();
    }

    private void SetThemeSelection(string themeName)
    {
        foreach (var item in ThemeComboBox.Items)
        {
            if (item is not ComboBoxItem comboBoxItem)
            {
                continue;
            }

            if (!string.Equals(comboBoxItem.Content?.ToString(), themeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ThemeComboBox.SelectedItem = comboBoxItem;
            return;
        }

        ThemeComboBox.SelectedIndex = 0;
    }

    private void UpdateCoagulatorStatusText()
    {
        CoagulatorStatusTextBlock.Text = _coagulatorProcessService.IsRunning
            ? "Coagulator: Running"
            : "Coagulator: Stopped";
    }


    private static bool IsSystemInDarkMode()
    {
        try
        {
            const string personalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            const string appsUseLightThemeValue = "AppsUseLightTheme";

            var appsUseLightTheme = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(personalizeKeyPath)?
                .GetValue(appsUseLightThemeValue);

            return appsUseLightTheme is int intValue && intValue == 0;
        }
        catch
        {
            return false;
        }
    }
}