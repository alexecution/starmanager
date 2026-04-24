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

        var providers = DiscoverProviders(providerDirectory, normalizedRoot, diagnostics);
        var starAppEntryPath = DetectStarUserAppEntry(userDirectory, normalizedRoot);
        var coagulatorEntryPath = DetectCoagulatorEntry(coagulatorDirectory, normalizedRoot);
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

    private static IReadOnlyList<ProviderItem> DiscoverProviders(string? providerDirectory, string rootDirectory, List<string> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(providerDirectory) || !Directory.Exists(providerDirectory))
        {
            diagnostics.Add("Provider directory not found, attempting root-folder provider discovery.");
            return DiscoverRootLevelProviders(rootDirectory, diagnostics);
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

    private static IReadOnlyList<ProviderItem> DiscoverRootLevelProviders(string rootDirectory, List<string> diagnostics)
    {
        if (!Directory.Exists(rootDirectory))
        {
            diagnostics.Add("Root-level provider discovery failed: root directory does not exist.");
            return [];
        }

        var providers = new List<ProviderItem>();

        var pythonProviderCandidates = Directory
            .GetFiles(rootDirectory, "*.py", SearchOption.TopDirectoryOnly)
            .Where(IsRootLevelProviderCandidate)
            .ToArray();

        // If provider scripts exist, prefer those and ignore helper executables in the same root.
        if (pythonProviderCandidates.Length > 0)
        {
            foreach (var filePath in pythonProviderCandidates)
            {
                providers.Add(new ProviderItem
                {
                    Name = Path.GetFileNameWithoutExtension(filePath),
                    FolderPath = rootDirectory,
                    EntryPath = filePath,
                    IsExecutable = false,
                });
            }

            diagnostics.Add($"Detected {providers.Count} root-level provider script(s); helper executables were ignored.");
            return providers
                .OrderBy(static provider => provider.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        // Fallback for binary-only layouts.
        foreach (var filePath in Directory.GetFiles(rootDirectory, "*.exe", SearchOption.TopDirectoryOnly)
                     .Where(IsRootLevelProviderCandidate))
        {
            var extension = Path.GetExtension(filePath);
            var providerName = Path.GetFileNameWithoutExtension(filePath);

            if (string.IsNullOrWhiteSpace(providerName))
            {
                continue;
            }

            providers.Add(new ProviderItem
            {
                Name = providerName,
                FolderPath = rootDirectory,
                EntryPath = filePath,
                IsExecutable = extension.Equals(".exe", StringComparison.OrdinalIgnoreCase),
            });
        }

        if (providers.Count == 0)
        {
            diagnostics.Add("No root-level provider entry files were detected.");
        }
        else
        {
            diagnostics.Add($"Detected {providers.Count} root-level provider(s).");
        }

        return providers
            .OrderBy(static provider => provider.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsRootLevelProviderCandidate(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (!extension.Equals(".py", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var providerName = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return false;
        }

        return !providerName.Equals("STAR", StringComparison.OrdinalIgnoreCase)
            && !providerName.Equals("coagulator", StringComparison.OrdinalIgnoreCase)
            && !providerName.Equals("provider", StringComparison.OrdinalIgnoreCase)
            && !providerName.Equals("readme", StringComparison.OrdinalIgnoreCase)
            && !providerName.Equals("html_readme", StringComparison.OrdinalIgnoreCase)
            && !providerName.Equals("build", StringComparison.OrdinalIgnoreCase)
            && !providerName.Equals("setup", StringComparison.OrdinalIgnoreCase)
            && !providerName.Equals("run", StringComparison.OrdinalIgnoreCase);
    }

    private static string? DetectStarUserAppEntry(string? userDirectory, string rootDirectory)
    {
        var candidateDirectories = new[]
        {
            userDirectory,
            rootDirectory,
        };

        foreach (var candidateDirectory in candidateDirectories)
        {
            if (string.IsNullOrWhiteSpace(candidateDirectory) || !Directory.Exists(candidateDirectory))
            {
                continue;
            }

            var preferredExe = Path.Combine(candidateDirectory, "STAR.exe");
            if (File.Exists(preferredExe))
            {
                return preferredExe;
            }

            var preferredPy = Path.Combine(candidateDirectory, "STAR.py");
            if (File.Exists(preferredPy))
            {
                return preferredPy;
            }
        }

        return null;
    }

    private static string? DetectCoagulatorEntry(string? coagulatorDirectory, string rootDirectory)
    {
        var candidateDirectories = new[]
        {
            coagulatorDirectory,
            rootDirectory,
        };

        foreach (var candidateDirectory in candidateDirectories)
        {
            if (string.IsNullOrWhiteSpace(candidateDirectory) || !Directory.Exists(candidateDirectory))
            {
                continue;
            }

            var preferredExe = Path.Combine(candidateDirectory, "coagulator.exe");
            if (File.Exists(preferredExe))
            {
                return preferredExe;
            }

            var preferredPy = Path.Combine(candidateDirectory, "coagulator.py");
            if (File.Exists(preferredPy))
            {
                return preferredPy;
            }
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
