using System.IO;
using StarManager.App.Models;

namespace StarManager.App.Services;

public sealed class StarDiscoveryService
{
    private const string ProviderDirectoryName = "provider";
    private const string ProvidersDirectoryName = "providers";
    private const string UserDirectoryName = "user";
    private const string CoagulatorDirectoryName = "coagulator";

    public StarScanResult Scan(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException("The selected STAR folder does not exist.");
        }

        var normalizedRoot = Path.GetFullPath(rootPath);
        var diagnostics = new List<string>
        {
            $"Scan root: {normalizedRoot}",
        };

        var providerDirectory = ResolveProviderDirectory(normalizedRoot, diagnostics);
        var coagulatorDirectory = ResolveDirectory(normalizedRoot, CoagulatorDirectoryName);
        var userDirectory = ResolveDirectory(normalizedRoot, UserDirectoryName);

        var providers = DiscoverProviders(providerDirectory, diagnostics);
        var starAppEntryPath = DetectStarUserAppEntry(userDirectory);
        var coagulatorEntryPath = DetectCoagulatorEntry(coagulatorDirectory);
        var coagulatorWebsiteUrl = DetectCoagulatorWebsiteUrl(coagulatorDirectory);

        diagnostics.Add($"Provider count: {providers.Count}");
        diagnostics.Add($"STAR app entry: {(starAppEntryPath ?? "not found")}");
        diagnostics.Add($"Coagulator entry: {(coagulatorEntryPath ?? "not found")}");
        diagnostics.Add($"Coagulator website URL: {(coagulatorWebsiteUrl ?? "not available")}");

        return new StarScanResult
        {
            RootPath = normalizedRoot,
            Providers = providers,
            StarAppEntryPath = starAppEntryPath,
            CoagulatorEntryPath = coagulatorEntryPath,
            CoagulatorWebsiteUrl = coagulatorWebsiteUrl,
            Diagnostics = diagnostics,
        };
    }

    private static string? ResolveProviderDirectory(string normalizedRoot, List<string> diagnostics)
    {
        var providerDirectory = ResolveDirectory(normalizedRoot, ProviderDirectoryName)
            ?? ResolveDirectory(normalizedRoot, ProvidersDirectoryName);

        if (!string.IsNullOrWhiteSpace(providerDirectory))
        {
            diagnostics.Add($"Provider directory: {providerDirectory}");
            return providerDirectory;
        }

        var folderName = Path.GetFileName(normalizedRoot);
        if (folderName.Equals(ProviderDirectoryName, StringComparison.OrdinalIgnoreCase)
            || folderName.Equals(ProvidersDirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add($"Provider directory inferred from selected folder name: {normalizedRoot}");
            return normalizedRoot;
        }

        diagnostics.Add("Provider directory not found (expected 'provider' or 'providers').");
        return null;
    }

    private static string? ResolveDirectory(string root, string childDirectoryName)
    {
        var direct = Path.Combine(root, childDirectoryName);
        return Directory.Exists(direct) ? direct : null;
    }

    private static IReadOnlyList<ProviderItem> DiscoverProviders(string? providerDirectory, List<string> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(providerDirectory) || !Directory.Exists(providerDirectory))
        {
            diagnostics.Add("Provider scan skipped: no provider directory exists.");
            return [];
        }

        var providers = new List<ProviderItem>();
        var seenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            if (!seenEntries.Add(entryPath))
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

        // Support STAR layouts where providers are files directly under the provider directory.
        foreach (var filePath in Directory.GetFiles(providerDirectory, "*.py", SearchOption.TopDirectoryOnly)
                     .Concat(Directory.GetFiles(providerDirectory, "*.exe", SearchOption.TopDirectoryOnly)))
        {
            var extension = Path.GetExtension(filePath);
            var providerName = Path.GetFileNameWithoutExtension(filePath);

            if (string.IsNullOrWhiteSpace(providerName)
                || providerName.Equals("provider", StringComparison.OrdinalIgnoreCase)
                || providerName.Equals("__init__", StringComparison.OrdinalIgnoreCase)
                || providerName.Equals("coagulator", StringComparison.OrdinalIgnoreCase)
                || providerName.Equals("STAR", StringComparison.OrdinalIgnoreCase)
                || providerName.Equals("readme", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!extension.Equals(".py", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!seenEntries.Add(filePath))
            {
                continue;
            }

            providers.Add(new ProviderItem
            {
                Name = providerName,
                FolderPath = providerDirectory,
                EntryPath = filePath,
                IsExecutable = extension.Equals(".exe", StringComparison.OrdinalIgnoreCase),
            });
        }

        if (providers.Count == 0)
        {
            diagnostics.Add($"No providers found in directory: {providerDirectory}");
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

    private static string? DetectStarUserAppEntry(string? userDirectory)
    {
        if (string.IsNullOrWhiteSpace(userDirectory) || !Directory.Exists(userDirectory))
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

    private static string? DetectCoagulatorEntry(string? coagulatorDirectory)
    {
        if (string.IsNullOrWhiteSpace(coagulatorDirectory) || !Directory.Exists(coagulatorDirectory))
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

    private static string? DetectCoagulatorWebsiteUrl(string? coagulatorDirectory)
    {
        if (string.IsNullOrWhiteSpace(coagulatorDirectory) || !Directory.Exists(coagulatorDirectory))
        {
            return null;
        }

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
