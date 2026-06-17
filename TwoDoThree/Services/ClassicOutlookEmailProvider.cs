using Microsoft.CSharp.RuntimeBinder;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using TwoDoThree.Models;

namespace TwoDoThree.Services;

public sealed class ClassicOutlookEmailProvider : IEmailProvider
{
    private const int OlFolderInbox = 6;
    private const int OlMailItemClass = 43;
    private const string AttachContentIdProperty = "http://schemas.microsoft.com/mapi/proptag/0x3712001F";
    private const string AttachMimeTagProperty = "http://schemas.microsoft.com/mapi/proptag/0x370E001F";
    private const string AttachDataBinaryProperty = "http://schemas.microsoft.com/mapi/proptag/0x37010102";

    private readonly IEmailCacheStore cacheStore;

    public ClassicOutlookEmailProvider(IEmailCacheStore cacheStore)
    {
        this.cacheStore = cacheStore;
    }

    public IReadOnlyList<EmailMessage> LoadCachedMessages()
    {
        return cacheStore.Load();
    }

    public async Task<EmailSyncResult> RefreshInboxAsync(
        EmailSettings settings,
        bool allowInteractiveSignIn,
        Window? owner,
        CancellationToken cancellationToken)
    {
        try
        {
            var messages = await Task.Run(
                () => ReadInbox(settings.MaxInboxMessages, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            cacheStore.Save(messages);
            return new EmailSyncResult(messages, true, $"Synced {messages.Count} Classic Outlook emails at {DateTime.Now:t}.");
        }
        catch (InvalidOperationException ex)
        {
            return new EmailSyncResult(cacheStore.Load(), false, ex.Message);
        }
        catch (COMException ex)
        {
            return new EmailSyncResult(cacheStore.Load(), false, $"Classic Outlook sync failed: {ex.Message}");
        }
    }

    public static bool IsClassicOutlookAvailable()
    {
        return Type.GetTypeFromProgID("Outlook.Application") is not null;
    }

    public static IReadOnlyList<EmailMessage> ReadInbox(int maxMessages, CancellationToken cancellationToken)
    {
        var outlookType = Type.GetTypeFromProgID("Outlook.Application")
                          ?? throw new InvalidOperationException("Classic Outlook is not installed or is not registered for COM automation.");
        dynamic outlook = Activator.CreateInstance(outlookType)
                          ?? throw new InvalidOperationException("Classic Outlook could not be started.");

        dynamic session = outlook.Session;
        dynamic inbox = session.GetDefaultFolder(OlFolderInbox);
        dynamic items = inbox.Items;
        items.Sort("[ReceivedTime]", true);

        var messages = new List<EmailMessage>();
        var count = Math.Min(Math.Clamp(maxMessages, 1, 250), (int)items.Count);
        for (var index = 1; index <= count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            dynamic item = items[index];
            if (TryMapMailItem(item, out EmailMessage message))
            {
                messages.Add(message);
            }
        }

        return messages;
    }

    public static bool TryMapSharedMessageFile(string filePath, out EmailMessage message)
    {
        message = default!;
        var outlookType = Type.GetTypeFromProgID("Outlook.Application");
        if (outlookType is null)
        {
            return false;
        }

        dynamic outlook = Activator.CreateInstance(outlookType)!;
        dynamic item = outlook.Session.OpenSharedItem(filePath);
        return TryMapMailItem(item, out message, $"manual-import:{EmailImportId.CreateForFile(filePath)}");
    }

    private static bool TryMapMailItem(dynamic item, out EmailMessage message, string? idOverride = null)
    {
        message = default!;
        try
        {
            if ((int)item.Class != OlMailItemClass)
            {
                return false;
            }

            var body = AsString(item.Body);
            var htmlBody = GetHtmlBody(item);
            var subject = AsString(item.Subject);
            var to = AsString(item.To);
            var cc = AsString(item.CC);
            var senderName = AsString(item.SenderName);
            var senderAddress = AsString(item.SenderEmailAddress);
            var from = string.IsNullOrWhiteSpace(senderName)
                ? senderAddress
                : string.IsNullOrWhiteSpace(senderAddress)
                    ? senderName
                    : $"{senderName} <{senderAddress}>";
            var receivedOn = item.ReceivedTime is DateTime receivedTime
                ? receivedTime
                : DateTime.Now;
            var entryId = AsString(item.EntryID);

            message = new EmailMessage
            {
                Id = idOverride ?? $"classic-outlook:{entryId}",
                From = from,
                To = to,
                Cc = cc,
                Subject = string.IsNullOrWhiteSpace(subject) ? "(No subject)" : subject,
                ReceivedOn = receivedOn,
                Preview = TextPreview.Create(body),
                Body = body,
                HtmlBody = EmbedInlineImages(htmlBody, item)
            };

            return true;
        }
        catch (RuntimeBinderException)
        {
            return false;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static string AsString(object? value)
    {
        return value?.ToString() ?? string.Empty;
    }

    private static string GetHtmlBody(dynamic item)
    {
        try
        {
            return AsString(item.HTMLBody);
        }
        catch (RuntimeBinderException)
        {
            return string.Empty;
        }
        catch (COMException)
        {
            return string.Empty;
        }
    }

    private static string EmbedInlineImages(string htmlBody, dynamic item)
    {
        if (string.IsNullOrWhiteSpace(htmlBody))
        {
            return string.Empty;
        }

        try
        {
            var images = new List<EmailInlineImage>();
            dynamic attachments = item.Attachments;
            var count = (int)attachments.Count;
            for (var index = 1; index <= count; index++)
            {
                dynamic attachment = attachments[index];
                var contentId = GetAttachmentPropertyString(attachment, AttachContentIdProperty);
                if (string.IsNullOrWhiteSpace(contentId))
                {
                    continue;
                }

                var contentType = GetAttachmentPropertyString(attachment, AttachMimeTagProperty);
                if (string.IsNullOrWhiteSpace(contentType))
                {
                    contentType = GuessContentType(AsString(attachment.FileName));
                }

                if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var base64Content = GetAttachmentBase64(attachment);
                if (!string.IsNullOrWhiteSpace(base64Content))
                {
                    images.Add(new EmailInlineImage(contentId, contentType, base64Content));
                }
            }

            return EmailBodyHtml.EmbedInlineImages(htmlBody, images);
        }
        catch (RuntimeBinderException)
        {
            return htmlBody;
        }
        catch (COMException)
        {
            return htmlBody;
        }
    }

    private static string GetAttachmentPropertyString(dynamic attachment, string propertyName)
    {
        try
        {
            return AsString(attachment.PropertyAccessor.GetProperty(propertyName));
        }
        catch (RuntimeBinderException)
        {
            return string.Empty;
        }
        catch (COMException)
        {
            return string.Empty;
        }
    }

    private static string GetAttachmentBase64(dynamic attachment)
    {
        try
        {
            var value = attachment.PropertyAccessor.GetProperty(AttachDataBinaryProperty);
            return value switch
            {
                byte[] bytes => Convert.ToBase64String(bytes),
                Array array => Convert.ToBase64String(array.Cast<byte>().ToArray()),
                _ => string.Empty
            };
        }
        catch (RuntimeBinderException)
        {
            return string.Empty;
        }
        catch (COMException)
        {
            return string.Empty;
        }
    }

    private static string GuessContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }
}
