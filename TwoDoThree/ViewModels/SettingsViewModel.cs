using System.Collections.ObjectModel;
using TwoDoThree.Models;

namespace TwoDoThree.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettings settings;
    private string selectedSection = "Email";
    private string connectionStatus = string.Empty;

    public SettingsViewModel(AppSettings settings)
    {
        this.settings = settings;
        Email = new EmailSettings
        {
            Source = settings.Email.Source,
            AccountAddress = settings.Email.AccountAddress,
            DisplayName = settings.Email.DisplayName,
            TenantId = settings.Email.TenantId,
            ClientId = settings.Email.ClientId,
            SyncIntervalMinutes = settings.Email.SyncIntervalMinutes,
            UseWindowsAuthentication = settings.Email.UseWindowsAuthentication,
            MaxInboxMessages = settings.Email.MaxInboxMessages
        };
        TagManager = new TagManagerViewModel(settings.Tags.Tags);
        Sections = new ObservableCollection<string> { "Email", "Tags" };
    }

    public EmailSettings Email { get; }

    public TagManagerViewModel TagManager { get; }

    public IReadOnlyList<EmailSource> EmailSourceValues { get; } = Enum.GetValues<EmailSource>();

    public ObservableCollection<string> Sections { get; }

    public string SelectedSection
    {
        get => selectedSection;
        set => SetProperty(ref selectedSection, value);
    }

    public string ConnectionStatus
    {
        get => connectionStatus;
        set => SetProperty(ref connectionStatus, value);
    }

    public void Apply()
    {
        settings.Email.AccountAddress = Email.AccountAddress;
        settings.Email.Source = Email.Source;
        settings.Email.DisplayName = Email.DisplayName;
        settings.Email.TenantId = Email.TenantId;
        settings.Email.ClientId = Email.ClientId;
        settings.Email.SyncIntervalMinutes = Email.SyncIntervalMinutes;
        settings.Email.UseWindowsAuthentication = Email.UseWindowsAuthentication;
        settings.Email.MaxInboxMessages = Email.MaxInboxMessages;
        TagManager.ApplyTo(settings.Tags);
    }

    public EmailSettings CreateEmailSettingsSnapshot()
    {
        return new EmailSettings
        {
            AccountAddress = Email.AccountAddress,
            Source = Email.Source,
            DisplayName = Email.DisplayName,
            TenantId = Email.TenantId,
            ClientId = Email.ClientId,
            SyncIntervalMinutes = Email.SyncIntervalMinutes,
            UseWindowsAuthentication = Email.UseWindowsAuthentication,
            MaxInboxMessages = Email.MaxInboxMessages
        };
    }
}
