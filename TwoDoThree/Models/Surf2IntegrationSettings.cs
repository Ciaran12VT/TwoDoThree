using TwoDoThree.ViewModels;

namespace TwoDoThree.Models;

public sealed class Surf2IntegrationSettings : ObservableObject
{
    private bool isEnabled;
    private string connectionString = string.Empty;
    private string executablePath = string.Empty;

    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (SetProperty(ref isEnabled, value))
            {
                OnPropertyChanged(nameof(IsConfigured));
            }
        }
    }

    public string ConnectionString
    {
        get => connectionString;
        set
        {
            if (SetProperty(ref connectionString, value))
            {
                OnPropertyChanged(nameof(IsConfigured));
            }
        }
    }

    public string ExecutablePath
    {
        get => executablePath;
        set => SetProperty(ref executablePath, value);
    }

    public bool IsConfigured => IsEnabled && !string.IsNullOrWhiteSpace(ConnectionString);
}
