using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
            var raw = File.ReadAllText(filePath, Encoding.UTF8);
            var (headers, body) = SplitHeadersAndBody(raw);
            var normalizedHeaders = ParseHeaders(headers);
            var subject = GetHeader(normalizedHeaders, "Subject");
            var from = GetHeader(normalizedHeaders, "From");
            var dateText = GetHeader(normalizedHeaders, "Date");
            var receivedOn = DateTime.TryParse(dateText, out var parsedDate)
                ? parsedDate
                : File.GetLastWriteTime(filePath);
            var cleanBody = CleanBody(body);

            message = new EmailMessage
            {
                Id = $"manual-import:{EmailImportId.CreateForFile(filePath)}",
                From = from,
                Subject = string.IsNullOrWhiteSpace(subject) ? Path.GetFileNameWithoutExtension(filePath) : subject,
                ReceivedOn = receivedOn,
                Preview = TextPreview.Create(cleanBody),
                Body = cleanBody
            };

            return true;
        }
        catch (IOException)
        {
            return false;
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
