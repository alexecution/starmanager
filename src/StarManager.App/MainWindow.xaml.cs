using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
    private string _providerSearchQuery = string.Empty;
    private bool _showOnlyNeedingSetup;
    private bool _isUpdatingRecentStarPathSelection;
    private HashSet<string> _initializedProviderEntryPaths = new(StringComparer.OrdinalIgnoreCase);

    private const int MaxRecentStarPaths = 10;

    public ObservableCollection<ProviderItem> Providers { get; } = [];
    public ObservableCollection<string> RecentStarPaths { get; } = [];
    public ICollectionView ProvidersView { get; }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        ProvidersView = CollectionViewSource.GetDefaultView(Providers);
        ProvidersView.Filter = ProviderMatchesSearchQuery;
        SourceInitialized += OnSourceInitialized;
        LoadSettings();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplyWindowChromeTheme(_isDarkThemeActive);
    }

    private void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        // Delay focus assignment until layout completes to avoid focus being stolen by template initialization.
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (BrowseButton.IsVisible && BrowseButton.IsEnabled)
            {
                _ = BrowseButton.Focus();
            }
        }, DispatcherPriority.Input);
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        if (e.Key is Key.D1 or Key.NumPad1)
        {
            MainSectionsTabControl.SelectedItem = ProvidersTabItem;
            e.Handled = true;
            return;
        }

        if (e.Key is Key.D2 or Key.NumPad2)
        {
            MainSectionsTabControl.SelectedItem = CoagulatorsTabItem;
            e.Handled = true;
        }
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
        AddRecentStarPath(_selectedStarRoot);
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
                provider.RequiresConfigureFirst = !_initializedProviderEntryPaths.Contains(provider.EntryPath);
                Providers.Add(provider);
            }

            ProvidersView.Refresh();
            ScanDiagnosticsTextBox.Text = string.Join(Environment.NewLine, _scanResult.Diagnostics);

            StatusTextBlock.Text =
                $"Scan complete: {_scanResult.Providers.Count} provider(s) found. " +
                $"STAR app: {(string.IsNullOrWhiteSpace(_scanResult.StarAppEntryPath) ? "missing" : "found")}. " +
                $"Coagulator: {(string.IsNullOrWhiteSpace(_scanResult.CoagulatorEntryPath) ? "missing" : "found")}.";
        }
        catch (Exception ex)
        {
            ScanDiagnosticsTextBox.Text = ex.ToString();
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
            MarkProviderInitialized(provider);
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
            MarkProviderInitialized(provider);
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

    private void ProviderSearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        _providerSearchQuery = ProviderSearchTextBox.Text.Trim();
        ProvidersView.Refresh();
    }

    private void ShowNeedingSetupOnlyCheckBox_OnCheckedChanged(object sender, RoutedEventArgs e)
    {
        _showOnlyNeedingSetup = ShowNeedingSetupOnlyCheckBox.IsChecked == true;
        ProvidersView.Refresh();
    }

    private void ClearProviderSearchButton_OnClick(object sender, RoutedEventArgs e)
    {
        ProviderSearchTextBox.Clear();
        _ = ProviderSearchTextBox.Focus();
    }

    private void RecentStarPathsComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingRecentStarPathSelection)
        {
            return;
        }

        if (RecentStarPathsComboBox.SelectedItem is not string selectedPath)
        {
            return;
        }

        if (!Directory.Exists(selectedPath))
        {
            StatusTextBlock.Text = "Selected recent STAR path no longer exists.";
            return;
        }

        _selectedStarRoot = selectedPath;
        StarPathTextBox.Text = selectedPath;
        AddRecentStarPath(selectedPath);
        StatusTextBlock.Text = "Recent STAR setup selected. Click Scan to refresh components.";
    }

    private bool ProviderMatchesSearchQuery(object item)
    {
        if (item is not ProviderItem provider)
        {
            return false;
        }

        if (_showOnlyNeedingSetup && !provider.RequiresConfigureFirst)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_providerSearchQuery))
        {
            return true;
        }

        return provider.Name.Contains(_providerSearchQuery, StringComparison.OrdinalIgnoreCase);
    }

    private void MarkProviderInitialized(ProviderItem provider)
    {
        provider.RequiresConfigureFirst = false;
        ProvidersView.Refresh();

        if (!_initializedProviderEntryPaths.Add(provider.EntryPath))
        {
            return;
        }

        _settings.InitializedProviderEntryPaths =
            _initializedProviderEntryPaths
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

        _settingsService.Save(_settings);
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
            SetThemeBrush("AppWindowBackgroundBrush", Color.FromRgb(22, 25, 31));
            SetThemeBrush("AppSurfaceBrush", Color.FromRgb(30, 35, 43));
            SetThemeBrush("AppControlBackgroundBrush", Color.FromRgb(37, 43, 53));
            SetThemeBrush("AppControlForegroundBrush", Color.FromRgb(243, 244, 246));
            SetThemeBrush("AppSecondaryForegroundBrush", Color.FromRgb(199, 206, 217));
            SetThemeBrush("AppBorderBrush", Color.FromRgb(150, 163, 184));
            SetThemeBrush("AppAccentBrush", Color.FromRgb(139, 184, 255));
            SetThemeBrush("AppDataGridAltRowBrush", Color.FromRgb(35, 42, 52));
            SetThemeBrush("AppSelectionBrush", Color.FromRgb(51, 90, 148));
            SetThemeBrush("AppControlHoverBrush", Color.FromRgb(45, 53, 66));
            SetThemeBrush("AppControlPressedBrush", Color.FromRgb(58, 71, 90));
            SetThemeBrush("AppDisabledBackgroundBrush", Color.FromRgb(47, 54, 66));
            SetThemeBrush("AppDisabledForegroundBrush", Color.FromRgb(164, 176, 194));
            SetThemeBrush("AppFocusBrush", Color.FromRgb(139, 184, 255));
            SetThemeBrush("AppTabActiveBackgroundBrush", Color.FromRgb(44, 67, 103));
            SetThemeBrush("AppBadgeRunningBackgroundBrush", Color.FromRgb(28, 58, 42));
            SetThemeBrush("AppBadgeRunningForegroundBrush", Color.FromRgb(185, 242, 206));
            SetThemeBrush("AppBadgeRunningBorderBrush", Color.FromRgb(63, 138, 99));
            SetThemeBrush("AppBadgeStoppedBackgroundBrush", Color.FromRgb(43, 50, 64));
            SetThemeBrush("AppBadgeStoppedForegroundBrush", Color.FromRgb(213, 217, 225));
            SetThemeBrush("AppBadgeStoppedBorderBrush", Color.FromRgb(139, 150, 170));
            return;
        }

        SetThemeBrush("AppWindowBackgroundBrush", Color.FromRgb(244, 246, 248));
        SetThemeBrush("AppSurfaceBrush", Color.FromRgb(255, 255, 255));
        SetThemeBrush("AppControlBackgroundBrush", Color.FromRgb(255, 255, 255));
        SetThemeBrush("AppControlForegroundBrush", Color.FromRgb(17, 24, 39));
        SetThemeBrush("AppSecondaryForegroundBrush", Color.FromRgb(55, 65, 81));
        SetThemeBrush("AppBorderBrush", Color.FromRgb(139, 147, 161));
        SetThemeBrush("AppAccentBrush", Color.FromRgb(29, 78, 216));
        SetThemeBrush("AppDataGridAltRowBrush", Color.FromRgb(238, 242, 247));
        SetThemeBrush("AppSelectionBrush", Color.FromRgb(220, 235, 255));
        SetThemeBrush("AppControlHoverBrush", Color.FromRgb(243, 247, 255));
        SetThemeBrush("AppControlPressedBrush", Color.FromRgb(215, 231, 255));
        SetThemeBrush("AppDisabledBackgroundBrush", Color.FromRgb(241, 243, 246));
        SetThemeBrush("AppDisabledForegroundBrush", Color.FromRgb(99, 107, 120));
        SetThemeBrush("AppFocusBrush", Color.FromRgb(29, 78, 216));
        SetThemeBrush("AppTabActiveBackgroundBrush", Color.FromRgb(231, 240, 255));
        SetThemeBrush("AppBadgeRunningBackgroundBrush", Color.FromRgb(232, 247, 238));
        SetThemeBrush("AppBadgeRunningForegroundBrush", Color.FromRgb(15, 81, 50));
        SetThemeBrush("AppBadgeRunningBorderBrush", Color.FromRgb(121, 198, 155));
        SetThemeBrush("AppBadgeStoppedBackgroundBrush", Color.FromRgb(241, 243, 246));
        SetThemeBrush("AppBadgeStoppedForegroundBrush", Color.FromRgb(55, 65, 81));
        SetThemeBrush("AppBadgeStoppedBorderBrush", Color.FromRgb(156, 163, 175));
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
            var darkCaptionColor = ToColorRef(Color.FromRgb(22, 25, 31));
            var darkBorderColor = ToColorRef(Color.FromRgb(22, 25, 31));
            var lightTextColor = ToColorRef(Color.FromRgb(243, 244, 246));

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
        _initializedProviderEntryPaths =
            _settings.InitializedProviderEntryPaths is { Count: > 0 }
                ? new HashSet<string>(_settings.InitializedProviderEntryPaths, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        RefreshRecentStarPaths(_settings.RecentStarRootPaths ?? []);

        if (!string.IsNullOrWhiteSpace(_settings.LastStarRootPath) && Directory.Exists(_settings.LastStarRootPath))
        {
            _selectedStarRoot = _settings.LastStarRootPath;
            StarPathTextBox.Text = _selectedStarRoot;
            if (RecentStarPaths.Contains(_selectedStarRoot))
            {
                RecentStarPathsComboBox.SelectedItem = _selectedStarRoot;
            }

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

    private void AddRecentStarPath(string path)
    {
        var fullPath = Path.GetFullPath(path);

        var existing = RecentStarPaths
            .FirstOrDefault(existingPath => string.Equals(existingPath, fullPath, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            _ = RecentStarPaths.Remove(existing);
        }

        RecentStarPaths.Insert(0, fullPath);

        while (RecentStarPaths.Count > MaxRecentStarPaths)
        {
            RecentStarPaths.RemoveAt(RecentStarPaths.Count - 1);
        }

        _settings.LastStarRootPath = fullPath;
        _settings.RecentStarRootPaths = RecentStarPaths.ToList();
        _settingsService.Save(_settings);

        _isUpdatingRecentStarPathSelection = true;
        try
        {
            RecentStarPathsComboBox.SelectedItem = fullPath;
        }
        finally
        {
            _isUpdatingRecentStarPathSelection = false;
        }
    }

    private void RefreshRecentStarPaths(IEnumerable<string> paths)
    {
        RecentStarPaths.Clear();

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                continue;
            }

            if (RecentStarPaths.Any(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            RecentStarPaths.Add(path);

            if (RecentStarPaths.Count >= MaxRecentStarPaths)
            {
                break;
            }
        }
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