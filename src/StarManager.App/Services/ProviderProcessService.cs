using System.Diagnostics;
using StarManager.App.Models;

namespace StarManager.App.Services;

public sealed class ProviderProcessService
{
    private readonly Dictionary<string, Process> _runningProviders = new(StringComparer.OrdinalIgnoreCase);

    public bool IsRunning(ProviderItem provider)
    {
        return _runningProviders.TryGetValue(provider.EntryPath, out var process)
            && !process.HasExited;
    }

    public void StartProvider(ProviderItem provider)
    {
        if (IsRunning(provider))
        {
            return;
        }

        var startInfo = BuildStartInfo(provider, []);
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start provider '{provider.Name}'.");

        _runningProviders[provider.EntryPath] = process;
    }

    public async Task StopProviderAsync(ProviderItem provider)
    {
        if (!_runningProviders.TryGetValue(provider.EntryPath, out var process))
        {
            return;
        }

        if (process.HasExited)
        {
            _runningProviders.Remove(provider.EntryPath);
            return;
        }

        try
        {
            process.CloseMainWindow();
        }
        catch
        {
            // Ignore and continue to forced termination path.
        }

        var exited = await WaitForExitAsync(process, TimeSpan.FromSeconds(2));
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            await WaitForExitAsync(process, TimeSpan.FromSeconds(2));
        }

        _runningProviders.Remove(provider.EntryPath);
    }

    public void LaunchConfigure(ProviderItem provider)
    {
        var startInfo = BuildStartInfo(provider, ["--configure"]);
        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not open configure UI for provider '{provider.Name}'.");
    }

    private static ProcessStartInfo BuildStartInfo(ProviderItem provider, string[] extraArguments)
    {
        var workingDirectory = provider.FolderPath;

        if (provider.IsExecutable)
        {
            return new ProcessStartInfo
            {
                FileName = provider.EntryPath,
                WorkingDirectory = workingDirectory,
                UseShellExecute = true,
                Arguments = string.Join(' ', extraArguments),
            };
        }

        return new ProcessStartInfo
        {
            FileName = "python",
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
            Arguments = $"\"{provider.EntryPath}\" {string.Join(' ', extraArguments)}",
        };
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        using var cancellationTokenSource = new CancellationTokenSource(timeout);

        try
        {
            await process.WaitForExitAsync(cancellationTokenSource.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
