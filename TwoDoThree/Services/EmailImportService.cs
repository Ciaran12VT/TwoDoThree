using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MimeKit;
using TwoDoThree.Models;

namespace TwoDoThree.Services;

public sealed class EmailImportService : IEmailImportService
{
    public Task<IReadOnlyList<EmailMessage>> ImportAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken)
    {
        var messages = new List<EmailMessage>();

        foreach (var filePath in filePaths.Where(File.Exists))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(filePath);
            if (extension.Equals(".eml", StringComparison.OrdinalIgnoreCase)
                && TryImportEml(filePath, out var emlMessage))
            {
                messages.Add(emlMessage);
            }
            else if (extension.Equals(".msg", StringComparison.OrdinalIgnoreCase)
                     && ClassicOutlookEmailProvider.TryMapSharedMessageFile(filePath, out var msgMessage))
            {
                messages.Add(msgMessage);
            }
        }

        return Task.FromResult<IReadOnlyList<EmailMessage>>(messages);
    }

    private static bool TryImportEml(string filePath, out EmailMessage message)
    {
        message = default!;
        try
        {
            var mimeMessage = MimeMessage.Load(filePath);
            var htmlBody = mimeMessage.HtmlBody ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(htmlBody))
            {
                htmlBody = EmailBodyHtml.EmbedInlineImages(htmlBody, GetInlineImages(mimeMessage));
            }

            var plainBody = mimeMessage.TextBody;
            if (string.IsNullOrWhiteSpace(plainBody))
            {
                plainBody = EmailBodyHtml.ToPlainText(htmlBody);
            }

            var receivedOn = mimeMessage.Date == DateTimeOffset.MinValue
                ? File.GetLastWriteTime(filePath)
                : mimeMessage.Date.LocalDateTime;

            message = new EmailMessage
            {
                Id = $"manual-import:{EmailImportId.CreateForFile(filePath)}",
                From = FormatAddresses(mimeMessage.From),
                To = FormatAddresses(mimeMessage.To),
                Cc = FormatAddresses(mimeMessage.Cc),
                Subject = string.IsNullOrWhiteSpace(mimeMessage.Subject) ? Path.GetFileNameWithoutExtension(filePath) : mimeMessage.Subject,
                ReceivedOn = receivedOn,
                Preview = TextPreview.Create(plainBody ?? string.Empty),
                Body = plainBody ?? string.Empty,
                HtmlBody = htmlBody
            };

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string FormatAddresses(InternetAddressList addresses)
    {
        return string.Join("; ", addresses.Select(address => address.ToString())
            .Where(address => !string.IsNullOrWhiteSpace(address)));
    }

    private static IReadOnlyList<EmailInlineImage> GetInlineImages(MimeMessage message)
    {
        return message.BodyParts
            .OfType<MimePart>()
            .Where(part => !string.IsNullOrWhiteSpace(part.ContentId)
                           && part.ContentType.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase))
            .Select(TryCreateInlineImage)
            .OfType<EmailInlineImage>()
            .ToList();
    }

    private static EmailInlineImage? TryCreateInlineImage(MimePart part)
    {
        try
        {
            if (part.Content is null || string.IsNullOrWhiteSpace(part.ContentId))
            {
                return null;
            }

            using var stream = new MemoryStream();
            part.Content.DecodeTo(stream);
            return new EmailInlineImage(
                part.ContentId,
                part.ContentType?.MimeType ?? "application/octet-stream",
                Convert.ToBase64String(stream.ToArray()));
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static (string Headers, string Body) SplitHeadersAndBody(string raw)
    {
        var separator = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var separatorLength = 4;
        if (separator < 0)
        {
            separator = raw.IndexOf("\n\n", StringComparison.Ordinal);
            separatorLength = 2;
        }

        return separator < 0
            ? (string.Empty, raw)
            : (raw[..separator], raw[(separator + separatorLength)..]);
    }

    private static Dictionary<string, string> ParseHeaders(string headers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentName = null;

        foreach (var line in headers.Replace("\r\n", "\n").Split('\n'))
        {
            if ((line.StartsWith(' ') || line.StartsWith('\t')) && currentName is not null)
            {
                result[currentName] += " " + line.Trim();
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            currentName = line[..separator].Trim();
            result[currentName] = line[(separator + 1)..].Trim();
        }

        return result;
    }

    private static string GetHeader(Dictionary<string, string> headers, string name)
    {
        return headers.TryGetValue(name, out var value) ? value : string.Empty;
    }

    private static string CleanBody(string body)
    {
        var trimmed = body.Trim();
        if (trimmed.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("<body", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = Regex.Replace(trimmed, "<br\\s*/?>", Environment.NewLine, RegexOptions.IgnoreCase);
            trimmed = Regex.Replace(trimmed, "</p>", Environment.NewLine + Environment.NewLine, RegexOptions.IgnoreCase);
            trimmed = Regex.Replace(trimmed, "<[^>]+>", string.Empty);
        }

        return trimmed;
    }
}
