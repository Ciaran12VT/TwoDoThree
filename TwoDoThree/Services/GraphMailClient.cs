using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwoDoThree.Models;

namespace TwoDoThree.Services;

public sealed class GraphMailClient : IGraphMailClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient httpClient;

    public GraphMailClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<IReadOnlyList<EmailMessage>> GetLatestInboxMessagesAsync(
        string accessToken,
        int maxMessages,
        CancellationToken cancellationToken)
    {
        var top = Math.Clamp(maxMessages, 1, 250);
        var requestUri = "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages"
                         + $"?$top={top}"
                         + "&$orderby=receivedDateTime%20desc"
                         + "&$select=id,conversationId,internetMessageId,subject,from,toRecipients,ccRecipients,receivedDateTime,bodyPreview,body,webLink,isRead,hasAttachments";

        using var response = await SendWithRetryAsync(requestUri, accessToken, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new GraphMailException(GetFriendlyErrorMessage(response.StatusCode, content));
        }

        var page = JsonSerializer.Deserialize<GraphMessagePage>(content, JsonOptions);
        var messages = new List<EmailMessage>();
        foreach (var message in page?.Value ?? [])
        {
            messages.Add(await MapMessageAsync(message, accessToken, cancellationToken).ConfigureAwait(false));
        }

        return messages
            .OrderByDescending(message => message.ReceivedOn)
            .ToList();
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        string requestUri,
        string accessToken,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var response = await SendAsync(requestUri, accessToken, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is not HttpStatusCode.TooManyRequests and not HttpStatusCode.ServiceUnavailable)
            {
                return response;
            }

            if (attempt == 3)
            {
                return response;
            }

            var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 * (attempt + 1));
            response.Dispose();
            await Task.Delay(retryAfter, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("Unexpected Graph retry state.");
    }

    private async Task<HttpResponseMessage> SendAsync(
        string requestUri,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("Prefer", "outlook.body-content-type=\"html\"");
        return await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<EmailMessage> MapMessageAsync(
        GraphMessage message,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var htmlBody = message.Body?.Content ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(htmlBody) && message.HasAttachments && !string.IsNullOrWhiteSpace(message.Id))
        {
            var inlineImages = await GetInlineImagesAsync(message.Id, accessToken, cancellationToken).ConfigureAwait(false);
            htmlBody = EmailBodyHtml.EmbedInlineImages(htmlBody, inlineImages);
        }

        var plainBody = EmailBodyHtml.ToPlainText(htmlBody);
        var preview = message.BodyPreview ?? string.Empty;
        return new EmailMessage
        {
            Id = message.Id ?? string.Empty,
            From = FormatSender(message.From?.EmailAddress),
            To = FormatRecipients(message.ToRecipients),
            Cc = FormatRecipients(message.CcRecipients),
            Subject = string.IsNullOrWhiteSpace(message.Subject) ? "(No subject)" : message.Subject,
            ReceivedOn = message.ReceivedDateTime?.LocalDateTime ?? DateTime.Now,
            Preview = string.IsNullOrWhiteSpace(preview) ? Truncate(plainBody, 180) : preview,
            Body = string.IsNullOrWhiteSpace(plainBody) ? preview : plainBody,
            HtmlBody = htmlBody
        };
    }

    private async Task<IReadOnlyList<EmailInlineImage>> GetInlineImagesAsync(
        string messageId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var requestUri = $"https://graph.microsoft.com/v1.0/me/messages/{Uri.EscapeDataString(messageId)}/attachments"
                         + "?$select=id,name,contentType,isInline,contentId,contentBytes";

        using var response = await SendWithRetryAsync(requestUri, accessToken, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var page = JsonSerializer.Deserialize<GraphAttachmentPage>(content, JsonOptions);
        return page?.Value
                   .Where(attachment => attachment.IsInline
                                        && !string.IsNullOrWhiteSpace(attachment.ContentId)
                                        && !string.IsNullOrWhiteSpace(attachment.ContentBytes))
                   .Select(attachment => new EmailInlineImage(
                       attachment.ContentId!,
                       string.IsNullOrWhiteSpace(attachment.ContentType) ? "application/octet-stream" : attachment.ContentType!,
                       attachment.ContentBytes!))
                   .ToList()
               ?? [];
    }

    private static string FormatSender(GraphEmailAddress? emailAddress)
    {
        if (emailAddress is null)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(emailAddress.Name))
        {
            return emailAddress.Address ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(emailAddress.Address))
        {
            return emailAddress.Name;
        }

        return $"{emailAddress.Name} <{emailAddress.Address}>";
    }

    private static string FormatRecipients(IEnumerable<GraphRecipient>? recipients)
    {
        return recipients is null
            ? string.Empty
            : string.Join("; ", recipients.Select(recipient => FormatSender(recipient.EmailAddress))
                .Where(recipient => !string.IsNullOrWhiteSpace(recipient)));
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength].TrimEnd() + "...";
    }

    private static string GetFriendlyErrorMessage(HttpStatusCode statusCode, string content)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => "Microsoft Graph rejected the sign-in token. Please reconnect the Outlook account.",
            HttpStatusCode.Forbidden => "Microsoft Graph denied access. Confirm the app has delegated Mail.Read permission and admin consent if required.",
            HttpStatusCode.TooManyRequests => "Microsoft Graph is throttling requests. Please wait and refresh again.",
            HttpStatusCode.ServiceUnavailable => "Microsoft Graph is temporarily unavailable. Please refresh again shortly.",
            _ => $"Microsoft Graph mail request failed with {(int)statusCode} {statusCode}. {content}"
        };
    }

    private sealed class GraphMessagePage
    {
        public List<GraphMessage> Value { get; set; } = [];
    }

    private sealed class GraphMessage
    {
        public string? Id { get; set; }

        public string? Subject { get; set; }

        public GraphRecipient? From { get; set; }

        public List<GraphRecipient> ToRecipients { get; set; } = [];

        public List<GraphRecipient> CcRecipients { get; set; } = [];

        public DateTimeOffset? ReceivedDateTime { get; set; }

        public string? BodyPreview { get; set; }

        public GraphBody? Body { get; set; }

        public bool HasAttachments { get; set; }
    }

    private sealed class GraphRecipient
    {
        public GraphEmailAddress? EmailAddress { get; set; }
    }

    private sealed class GraphEmailAddress
    {
        public string? Name { get; set; }

        public string? Address { get; set; }
    }

    private sealed class GraphBody
    {
        public string? Content { get; set; }

        public string? ContentType { get; set; }
    }

    private sealed class GraphAttachmentPage
    {
        public List<GraphAttachment> Value { get; set; } = [];
    }

    private sealed class GraphAttachment
    {
        [JsonPropertyName("@odata.type")]
        public string? ODataType { get; set; }

        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? ContentType { get; set; }

        public bool IsInline { get; set; }

        public string? ContentId { get; set; }

        public string? ContentBytes { get; set; }
    }
}
