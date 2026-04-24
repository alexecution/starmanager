using System.IO;
using System.Text.Json;
using StarManager.App.Models;

namespace StarManager.App.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _settingsFilePath;

    public SettingsService()
    {
        var roamingBaseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StarManager");

        Directory.CreateDirectory(roamingBaseDirectory);
        _settingsFilePath = Path.Combine(roamingBaseDirectory, "settings.json");

        TryMigrateFromLocalAppData(_settingsFilePath);
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsFilePath, json);
    }

    private static void TryMigrateFromLocalAppData(string roamingSettingsFilePath)
    {
        if (File.Exists(roamingSettingsFilePath))
        {
            return;
        }

        var localSettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StarManager",
            "settings.json");

        if (!File.Exists(localSettingsFilePath))
        {
            return;
        }

        try
        {
            File.Copy(localSettingsFilePath, roamingSettingsFilePath, overwrite: false);
        }
        catch
        {
            // Fall back to defaults if migration fails; app continues normally.
        }
    }
}
