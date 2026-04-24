using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StarManager.App.Models;

public sealed class ProviderItem : INotifyPropertyChanged
{
    private string _statusText = "Stopped";

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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
