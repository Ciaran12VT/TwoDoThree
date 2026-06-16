using Microsoft.CSharp.RuntimeBinder;
using System.Runtime.InteropServices;
using System.Windows;
using TwoDoThree.Models;

namespace TwoDoThree.Services;

public sealed class ClassicOutlookEmailProvider : IEmailProvider
{
    private const int OlFolderInbox = 6;
    private const int OlMailItemClass = 43;

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
            var subject = AsString(item.Subject);
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
                Subject = string.IsNullOrWhiteSpace(subject) ? "(No subject)" : subject,
                ReceivedOn = receivedOn,
                Preview = TextPreview.Create(body),
                Body = body
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
}
