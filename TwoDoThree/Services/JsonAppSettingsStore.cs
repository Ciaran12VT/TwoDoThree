using System.IO;
using System.Text.Json;
using TwoDoThree.Models;

namespace TwoDoThree.Services;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Load()
    {
        var settings = new AppSettings();
        if (!File.Exists(AppStoragePaths.SettingsFilePath))
        {
            return settings;
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<AppSettingsSnapshot>(
                File.ReadAllText(AppStoragePaths.SettingsFilePath),
                JsonOptions);
            if (snapshot is null)
            {
                return settings;
            }

            ApplyEmail(settings.Email, snapshot.Email);
            settings.Tags.ReplaceTags(snapshot.Tags);
            settings.Database.ConnectionString = snapshot.Database.ConnectionString;
            settings.Surf2.IsEnabled = snapshot.Surf2.IsEnabled;
            settings.Surf2.ConnectionString = snapshot.Surf2.ConnectionString;
            settings.Surf2.ExecutablePath = snapshot.Surf2.ExecutablePath;
        }
        catch (JsonException)
        {
            return settings;
        }
        catch (IOException)
        {
            return settings;
        }

        return settings;
    }

    public void Save(AppSettings settings)
    {
        AppStoragePaths.EnsureRootDirectory();
        var snapshot = new AppSettingsSnapshot
        {
            Email = EmailSettingsSnapshot.From(settings.Email),
            Tags = settings.Tags.Tags.ToList(),
            Database = DatabaseSettingsSnapshot.From(settings.Database),
            Surf2 = Surf2IntegrationSettingsSnapshot.From(settings.Surf2)
        };

        File.WriteAllText(
            AppStoragePaths.SettingsFilePath,
            JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    private static void ApplyEmail(EmailSettings settings, EmailSettingsSnapshot snapshot)
    {
        settings.Source = snapshot.Source;
        settings.AccountAddress = snapshot.AccountAddress;
        settings.DisplayName = snapshot.DisplayName;
        settings.TenantId = snapshot.TenantId;
        settings.ClientId = snapshot.ClientId;
        settings.SyncIntervalMinutes = snapshot.SyncIntervalMinutes;
        settings.UseWindowsAuthentication = snapshot.UseWindowsAuthentication;
        settings.MaxInboxMessages = snapshot.MaxInboxMessages;
    }

    private sealed class AppSettingsSnapshot
    {
        public EmailSettingsSnapshot Email { get; set; } = new();

        public List<string> Tags { get; set; } = [];

        public DatabaseSettingsSnapshot Database { get; set; } = new();

        public Surf2IntegrationSettingsSnapshot Surf2 { get; set; } = new();
    }

    private sealed class DatabaseSettingsSnapshot
    {
        public string ConnectionString { get; set; } = string.Empty;

        public static DatabaseSettingsSnapshot From(DatabaseSettings settings)
        {
            return new DatabaseSettingsSnapshot
            {
                ConnectionString = settings.ConnectionString
            };
        }
    }

    private sealed class Surf2IntegrationSettingsSnapshot
    {
        public bool IsEnabled { get; set; }

        public string ConnectionString { get; set; } = string.Empty;

        public string ExecutablePath { get; set; } = string.Empty;

        public static Surf2IntegrationSettingsSnapshot From(Surf2IntegrationSettings settings)
        {
            return new Surf2IntegrationSettingsSnapshot
            {
                IsEnabled = settings.IsEnabled,
                ConnectionString = settings.ConnectionString,
                ExecutablePath = settings.ExecutablePath
            };
        }
    }
}
