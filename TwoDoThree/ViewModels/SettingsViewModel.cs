using System.Collections.ObjectModel;
using TwoDoThree.Models;

namespace TwoDoThree.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettings settings;
    private string selectedSection = "Email";

    public SettingsViewModel(AppSettings settings)
    {
        this.settings = settings;
        Email = new EmailSettings
        {
            AccountAddress = settings.Email.AccountAddress,
            DisplayName = settings.Email.DisplayName,
            TenantId = settings.Email.TenantId,
            ClientId = settings.Email.ClientId,
            SyncIntervalMinutes = settings.Email.SyncIntervalMinutes,
            UseWindowsAuthentication = settings.Email.UseWindowsAuthentication
        };
        Sections = new ObservableCollection<string> { "Email" };
    }

    public EmailSettings Email { get; }

    public ObservableCollection<string> Sections { get; }

    public string SelectedSection
    {
        get => selectedSection;
        set => SetProperty(ref selectedSection, value);
    }

    public void Apply()
    {
        settings.Email.AccountAddress = Email.AccountAddress;
        settings.Email.DisplayName = Email.DisplayName;
        settings.Email.TenantId = Email.TenantId;
        settings.Email.ClientId = Email.ClientId;
        settings.Email.SyncIntervalMinutes = Email.SyncIntervalMinutes;
        settings.Email.UseWindowsAuthentication = Email.UseWindowsAuthentication;
    }
}
