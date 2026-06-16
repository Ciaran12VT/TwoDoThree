using System.IO;
using System.Text.Json;
using TwoDoThree.Models;

namespace TwoDoThree.Services;

public sealed class JsonEmailCacheStore : IEmailCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public IReadOnlyList<EmailMessage> Load()
    {
        if (!File.Exists(AppStoragePaths.EmailCacheFilePath))
        {
            return [];
        }

        try
        {
            var snapshots = JsonSerializer.Deserialize<List<EmailMessageSnapshot>>(
                File.ReadAllText(AppStoragePaths.EmailCacheFilePath),
                JsonOptions);

            return snapshots?
                       .Select(snapshot => snapshot.ToEmailMessage())
                       .OrderByDescending(message => message.ReceivedOn)
                       .ToList()
                   ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<EmailMessage> messages)
    {
        AppStoragePaths.EnsureRootDirectory();
        var snapshots = messages.Select(EmailMessageSnapshot.FromEmail).ToList();
        File.WriteAllText(
            AppStoragePaths.EmailCacheFilePath,
            JsonSerializer.Serialize(snapshots, JsonOptions));
    }

    public void Clear()
    {
        if (File.Exists(AppStoragePaths.EmailCacheFilePath))
        {
            File.Delete(AppStoragePaths.EmailCacheFilePath);
        }
    }

    private sealed class EmailMessageSnapshot
    {
        public string Id { get; set; } = string.Empty;

        public string From { get; set; } = string.Empty;

        public string Subject { get; set; } = string.Empty;

        public DateTime ReceivedOn { get; set; }

        public string Preview { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;

        public static EmailMessageSnapshot FromEmail(EmailMessage message)
        {
            return new EmailMessageSnapshot
            {
                Id = message.Id,
                From = message.From,
                Subject = message.Subject,
                ReceivedOn = message.ReceivedOn,
                Preview = message.Preview,
                Body = message.Body
            };
        }

        public EmailMessage ToEmailMessage()
        {
            return new EmailMessage
            {
                Id = Id,
                From = From,
                Subject = Subject,
                ReceivedOn = ReceivedOn,
                Preview = Preview,
                Body = Body
            };
        }
    }
}
