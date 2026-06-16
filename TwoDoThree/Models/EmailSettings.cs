using TwoDoThree.ViewModels;

namespace TwoDoThree.Models;

public sealed class EmailSettings : ObservableObject
{
    private EmailSource source = EmailSource.MicrosoftGraph;
    private string accountAddress = string.Empty;
    private string displayName = string.Empty;
    private string tenantId = string.Empty;
    private string clientId = string.Empty;
    private bool useWindowsAuthentication = true;
    private int syncIntervalMinutes = 15;
    private int maxInboxMessages = 100;

    public EmailSource Source
    {
        get => source;
        set
        {
            if (SetProperty(ref source, value))
            {
                OnPropertyChanged(nameof(IsConfigured));
            }
        }
    }

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
        set
        {
            if (SetProperty(ref clientId, value))
            {
                OnPropertyChanged(nameof(IsConfigured));
            }
        }
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

    public int MaxInboxMessages
    {
        get => maxInboxMessages;
        set => SetProperty(ref maxInboxMessages, Math.Clamp(value, 1, 250));
    }

    public bool IsConfigured => Source != EmailSource.MicrosoftGraph || !string.IsNullOrWhiteSpace(ClientId);
}
