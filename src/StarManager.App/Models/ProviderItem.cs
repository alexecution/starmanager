using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StarManager.App.Models;

public sealed class ProviderItem : INotifyPropertyChanged
{
    private string _statusText = "Stopped";
    private bool _requiresConfigureFirst = true;

    public required string Name { get; init; }

    public required string FolderPath { get; init; }

    public required string EntryPath { get; init; }

    public required bool IsExecutable { get; init; }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged();
        }
    }

    public bool RequiresConfigureFirst
    {
        get => _requiresConfigureFirst;
        set
        {
            if (_requiresConfigureFirst == value)
            {
                return;
            }

            _requiresConfigureFirst = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FirstLaunchHint));
        }
    }

    public string FirstLaunchHint => RequiresConfigureFirst ? "Configure first" : string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public override string ToString()
    {
        return Name;
    }
}
