namespace StarManager.App.Models;

public sealed class AppSettings
{
    public string ThemeName { get; set; } = "System";

    public string? LastStarRootPath { get; set; }

    public List<string> RecentStarRootPaths { get; set; } = [];

    public List<string> InitializedProviderEntryPaths { get; set; } = [];
}
