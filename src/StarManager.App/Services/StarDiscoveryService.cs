using System.IO;
using StarManager.App.Models;

namespace StarManager.App.Services;

public sealed class StarDiscoveryService
{
    private const string ProviderDirectoryName = "provider";
    private const string UserDirectoryName = "user";
    private const string CoagulatorDirectoryName = "coagulator";

    public StarScanResult Scan(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException("The selected STAR folder does not exist.");
        }

        var normalizedRoot = Path.GetFullPath(rootPath);
        var providerDirectory = Path.Combine(normalizedRoot, ProviderDirectoryName);
        var coagulatorDirectory = Path.Combine(normalizedRoot, CoagulatorDirectoryName);
        var userDirectory = Path.Combine(normalizedRoot, UserDirectoryName);

        var providers = DiscoverProviders(providerDirectory);
        var starAppEntryPath = DetectStarUserAppEntry(userDirectory);
        var coagulatorEntryPath = DetectCoagulatorEntry(coagulatorDirectory);
        var coagulatorWebsiteUrl = DetectCoagulatorWebsiteUrl(coagulatorDirectory);

        return new StarScanResult
        {
            RootPath = normalizedRoot,
            Providers = providers,
            StarAppEntryPath = starAppEntryPath,
            CoagulatorEntryPath = coagulatorEntryPath,
            CoagulatorWebsiteUrl = coagulatorWebsiteUrl,
        };
    }

    private static IReadOnlyList<ProviderItem> DiscoverProviders(string providerDirectory)
    {
        if (!Directory.Exists(providerDirectory))
        {
            return [];
        }

        var providers = new List<ProviderItem>();

        foreach (var directory in Directory.GetDirectories(providerDirectory))
        {
            var folderName = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                continue;
            }

            var entryPath = FindProviderEntrypoint(directory, folderName);
            if (entryPath is null)
            {
                continue;
            }

            providers.Add(new ProviderItem
            {
                Name = folderName,
                FolderPath = directory,
                EntryPath = entryPath,
                IsExecutable = Path.GetExtension(entryPath).Equals(".exe", StringComparison.OrdinalIgnoreCase),
            });
        }

        return providers
            .OrderBy(static provider => provider.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? FindProviderEntrypoint(string providerDirectory, string providerName)
    {
        var preferredExe = Path.Combine(providerDirectory, $"{providerName}.exe");
        if (File.Exists(preferredExe))
        {
            return preferredExe;
        }

        var preferredPy = Path.Combine(providerDirectory, $"{providerName}.py");
        if (File.Exists(preferredPy))
        {
            return preferredPy;
        }

        var anyExe = Directory
            .GetFiles(providerDirectory, "*.exe", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(anyExe))
        {
            return anyExe;
        }

        return Directory
            .GetFiles(providerDirectory, "*.py", SearchOption.TopDirectoryOnly)
            .Where(static path => !Path.GetFileName(path).Equals("provider.py", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string? DetectStarUserAppEntry(string userDirectory)
    {
        if (!Directory.Exists(userDirectory))
        {
            return null;
        }

        var preferredExe = Path.Combine(userDirectory, "STAR.exe");
        if (File.Exists(preferredExe))
        {
            return preferredExe;
        }

        var preferredPy = Path.Combine(userDirectory, "STAR.py");
        if (File.Exists(preferredPy))
        {
            return preferredPy;
        }

        return null;
    }

    private static string? DetectCoagulatorEntry(string coagulatorDirectory)
    {
        if (!Directory.Exists(coagulatorDirectory))
        {
            return null;
        }

        var preferredExe = Path.Combine(coagulatorDirectory, "coagulator.exe");
        if (File.Exists(preferredExe))
        {
            return preferredExe;
        }

        var preferredPy = Path.Combine(coagulatorDirectory, "coagulator.py");
        if (File.Exists(preferredPy))
        {
            return preferredPy;
        }

        return null;
    }

    private static string? DetectCoagulatorWebsiteUrl(string coagulatorDirectory)
    {
        var coagulatorIniPath = Path.Combine(coagulatorDirectory, "coagulator.ini");
        if (!File.Exists(coagulatorIniPath))
        {
            return null;
        }

        var bindAddress = "localhost";
        var bindPort = "7774";
        var httpFrontendEnabled = true;

        foreach (var rawLine in File.ReadAllLines(coagulatorIniPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();

            if (key.Equals("bind_address", StringComparison.OrdinalIgnoreCase))
            {
                bindAddress = value;
                continue;
            }

            if (key.Equals("bind_port", StringComparison.OrdinalIgnoreCase))
            {
                bindPort = value;
                continue;
            }

            if (key.Equals("http_frontend", StringComparison.OrdinalIgnoreCase))
            {
                httpFrontendEnabled = !value.Equals("false", StringComparison.OrdinalIgnoreCase)
                    && !value.Equals("0", StringComparison.OrdinalIgnoreCase)
                    && !value.Equals("no", StringComparison.OrdinalIgnoreCase);
            }
        }

        if (!httpFrontendEnabled)
        {
            return null;
        }

        if (bindAddress is "0.0.0.0" or "::")
        {
            bindAddress = "localhost";
        }

        return $"http://{bindAddress}:{bindPort}";
    }
}
