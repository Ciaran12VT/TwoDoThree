using System.Collections.ObjectModel;
using TwoDoThree.Models;

namespace TwoDoThree.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettings settings;
    private string selectedSection = "Email";
    private string connectionStatus = string.Empty;
    private string storageStatus = string.Empty;
    private string surf2Status = string.Empty;

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
        Database = new DatabaseSettings
        {
            ConnectionString = settings.Database.ConnectionString
        };
        Surf2 = new Surf2IntegrationSettings
        {
            IsEnabled = settings.Surf2.IsEnabled,
            ConnectionString = settings.Surf2.ConnectionString,
            ExecutablePath = settings.Surf2.ExecutablePath
        };
        Sections = new ObservableCollection<string> { "Email", "Tags", "Storage", "Surf2" };
        ConnectionStatus = GetDefaultConnectionStatus();
        StorageStatus = Database.IsConfigured
            ? "SQL Server connection string configured."
            : "Enter a SQL Server connection string to enable task persistence.";
        Surf2Status = Surf2.IsConfigured
            ? "Surf2 integration configured."
            : "Enable Surf2 and enter its database connection string.";
        Email.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EmailSettings.Source))
            {
                ConnectionStatus = GetDefaultConnectionStatus();
            }
        };
    }

    public EmailSettings Email { get; }

    public TagManagerViewModel TagManager { get; }

    public DatabaseSettings Database { get; }

    public Surf2IntegrationSettings Surf2 { get; }

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

    public string StorageStatus
    {
        get => storageStatus;
        set => SetProperty(ref storageStatus, value);
    }

    public string Surf2Status
    {
        get => surf2Status;
        set => SetProperty(ref surf2Status, value);
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
        settings.Database.ConnectionString = Database.ConnectionString;
        settings.Surf2.IsEnabled = Surf2.IsEnabled;
        settings.Surf2.ConnectionString = Surf2.ConnectionString;
        settings.Surf2.ExecutablePath = Surf2.ExecutablePath;
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

    private string GetDefaultConnectionStatus()
    {
        return Email.Source switch
        {
            EmailSource.MicrosoftGraph => "Microsoft Graph selected. Enter the Entra client ID, then connect.",
            EmailSource.ClassicOutlook => "Classic Outlook selected. The app will use the local default Outlook profile.",
            EmailSource.ManualImport => "Manual import selected. Import .eml or .msg files from the main Email panel.",
            _ => string.Empty
        };
    }
}
