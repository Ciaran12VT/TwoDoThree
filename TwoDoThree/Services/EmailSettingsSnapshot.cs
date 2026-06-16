using TwoDoThree.Models;

namespace TwoDoThree.Services;

public sealed class EmailSettingsSnapshot
{
    public EmailSource Source { get; set; } = EmailSource.MicrosoftGraph;

    public string AccountAddress { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public bool UseWindowsAuthentication { get; set; } = true;

    public int SyncIntervalMinutes { get; set; } = 15;

    public int MaxInboxMessages { get; set; } = 100;

    public static EmailSettingsSnapshot From(EmailSettings settings)
    {
        return new EmailSettingsSnapshot
        {
            AccountAddress = settings.AccountAddress,
            Source = settings.Source,
            DisplayName = settings.DisplayName,
            TenantId = settings.TenantId,
            ClientId = settings.ClientId,
            SyncIntervalMinutes = settings.SyncIntervalMinutes,
            UseWindowsAuthentication = settings.UseWindowsAuthentication,
            MaxInboxMessages = settings.MaxInboxMessages
        };
    }

    public EmailSettings ToSettings()
    {
        return new EmailSettings
        {
            AccountAddress = AccountAddress,
            Source = Source,
            DisplayName = DisplayName,
            TenantId = TenantId,
            ClientId = ClientId,
            SyncIntervalMinutes = SyncIntervalMinutes,
            UseWindowsAuthentication = UseWindowsAuthentication,
            MaxInboxMessages = MaxInboxMessages
        };
    }
}
