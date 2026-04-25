using System.IO;
using StarManager.App.Models;

namespace StarManager.App.Services;

public sealed class ProviderIniEditorService
{
    private static readonly string[] PreferredIniFileNames =
    [
        "provider.ini",
        "settings.ini",
        "config.ini",
    ];

    public string? FindLikelyIniFile(ProviderItem provider)
    {
        if (!Directory.Exists(provider.FolderPath))
        {
            return null;
        }

        var iniFiles = Directory
            .GetFiles(provider.FolderPath, "*.ini", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (iniFiles.Length == 0)
        {
            return null;
        }

        var entryBaseName = Path.GetFileNameWithoutExtension(provider.EntryPath);
        var providerName = provider.Name;

        foreach (var iniFile in iniFiles)
        {
            var iniName = Path.GetFileNameWithoutExtension(iniFile);
            if (iniName.Equals(entryBaseName, StringComparison.OrdinalIgnoreCase)
                || iniName.Equals(providerName, StringComparison.OrdinalIgnoreCase))
            {
                return iniFile;
            }
        }

        foreach (var preferredName in PreferredIniFileNames)
        {
            var preferredPath = iniFiles.FirstOrDefault(path =>
                Path.GetFileName(path).Equals(preferredName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(preferredPath))
            {
                return preferredPath;
            }
        }

        return iniFiles[0];
    }

    public string ReadIniFile(string iniPath)
    {
        return File.ReadAllText(iniPath);
    }

    public bool HasConfiguredValues(ProviderItem provider)
    {
        var iniPath = FindLikelyIniFile(provider);
        if (string.IsNullOrWhiteSpace(iniPath) || !File.Exists(iniPath))
        {
            return false;
        }

        return IniHasConfiguredValues(iniPath);
    }

    public string? SaveIniFileSafely(string iniPath, string content)
    {
        var directory = Path.GetDirectoryName(iniPath)
            ?? throw new InvalidOperationException("INI path has no parent directory.");

        Directory.CreateDirectory(directory);

        var tempFilePath = Path.Combine(directory, $".{Path.GetFileName(iniPath)}.tmp");
        var backupFilePath = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(iniPath)}.{DateTime.Now:yyyyMMddHHmmss}.bak.ini");

        File.WriteAllText(tempFilePath, content);

        if (File.Exists(iniPath))
        {
            File.Replace(tempFilePath, iniPath, backupFilePath, ignoreMetadataErrors: true);
            return backupFilePath;
        }

        File.Move(tempFilePath, iniPath);
        return null;
    }

    private static bool IniHasConfiguredValues(string iniPath)
    {
        var hasSettings = false;

        foreach (var rawLine in File.ReadLines(iniPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0
                || line.StartsWith(';')
                || line.StartsWith('#')
                || (line.StartsWith('[') && line.EndsWith(']')))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            hasSettings = true;

            var value = line[(separatorIndex + 1)..].Trim();
            if (value.Length == 0)
            {
                return false;
            }
        }

        return hasSettings;
    }
}
