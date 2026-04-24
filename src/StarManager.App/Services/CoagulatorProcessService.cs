using System.Diagnostics;
using System.IO;

namespace StarManager.App.Services;

public sealed class CoagulatorProcessService
{
    private Process? _coagulatorProcess;

    public bool IsRunning => _coagulatorProcess is not null && !_coagulatorProcess.HasExited;

    public void Start(string entryPath)
    {
        if (IsRunning)
        {
            return;
        }

        var startInfo = BuildStartInfo(entryPath);
        _coagulatorProcess = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start coagulator process.");
    }

    public async Task StopAsync()
    {
        if (_coagulatorProcess is null)
        {
            return;
        }

        if (_coagulatorProcess.HasExited)
        {
            _coagulatorProcess = null;
            return;
        }

        try
        {
            _coagulatorProcess.CloseMainWindow();
        }
        catch
        {
            // Ignore and continue to forced termination path.
        }

        var exited = await WaitForExitAsync(_coagulatorProcess, TimeSpan.FromSeconds(2));
        if (!exited)
        {
            _coagulatorProcess.Kill(entireProcessTree: true);
            await WaitForExitAsync(_coagulatorProcess, TimeSpan.FromSeconds(2));
        }

        _coagulatorProcess = null;
    }

    private static ProcessStartInfo BuildStartInfo(string entryPath)
    {
        var directory = Path.GetDirectoryName(entryPath)
            ?? throw new InvalidOperationException("Coagulator entrypoint path has no parent directory.");

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
            FileName = "python",
            WorkingDirectory = directory,
            UseShellExecute = true,
            Arguments = $"\"{entryPath}\"",
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
