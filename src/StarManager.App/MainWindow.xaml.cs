using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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

    public ObservableCollection<ProviderItem> Providers { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        LoadSettings();
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

        var windowBackground = useDarkTheme ? Color.FromRgb(26, 28, 32) : Color.FromRgb(248, 249, 251);
        var textForeground = useDarkTheme ? Color.FromRgb(230, 233, 238) : Color.FromRgb(23, 28, 34);

        Background = new SolidColorBrush(windowBackground);
        Foreground = new SolidColorBrush(textForeground);
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