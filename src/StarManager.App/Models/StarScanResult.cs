namespace StarManager.App.Models;

public sealed class StarScanResult
{
    public string RootPath { get; init; } = string.Empty;

    public string? StarAppEntryPath { get; init; }

    public string? CoagulatorEntryPath { get; init; }

    public string? CoagulatorWebsiteUrl { get; init; }

    public IReadOnlyList<ProviderItem> Providers { get; init; } = [];
}
