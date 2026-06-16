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
            Tags = settings.Tags.Tags.ToList()
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
    }
}
