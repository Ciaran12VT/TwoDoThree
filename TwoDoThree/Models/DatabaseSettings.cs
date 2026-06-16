using TwoDoThree.ViewModels;

namespace TwoDoThree.Models;

public sealed class DatabaseSettings : ObservableObject
{
    private string connectionString = string.Empty;

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

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);
}
