using TwoDoThree.ViewModels;

namespace TwoDoThree.Models;

public sealed class EmailSettings : ObservableObject
{
    private string accountAddress = "demo.outlook@example.com";
    private string displayName = "Outlook Demo";
    private string tenantId = string.Empty;
    private string clientId = string.Empty;
    private bool useWindowsAuthentication = true;
    private int syncIntervalMinutes = 15;

    public string AccountAddress
    {
        get => accountAddress;
        set
        {
            if (SetProperty(ref accountAddress, value))
            {
                OnPropertyChanged(nameof(IsConfigured));
            }
        }
    }

    public string DisplayName
    {
        get => displayName;
        set => SetProperty(ref displayName, value);
    }

    public string TenantId
    {
        get => tenantId;
        set => SetProperty(ref tenantId, value);
    }

    public string ClientId
    {
        get => clientId;
        set => SetProperty(ref clientId, value);
    }

    public bool UseWindowsAuthentication
    {
        get => useWindowsAuthentication;
        set => SetProperty(ref useWindowsAuthentication, value);
    }

    public int SyncIntervalMinutes
    {
        get => syncIntervalMinutes;
        set => SetProperty(ref syncIntervalMinutes, Math.Max(1, value));
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(AccountAddress);
}
