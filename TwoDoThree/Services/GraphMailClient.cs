using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
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
                         + "&$select=id,conversationId,internetMessageId,subject,from,receivedDateTime,bodyPreview,body,webLink,isRead,hasAttachments";

        using var response = await SendWithRetryAsync(requestUri, accessToken, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new GraphMailException(GetFriendlyErrorMessage(response.StatusCode, content));
        }

        var page = JsonSerializer.Deserialize<GraphMessagePage>(content, JsonOptions);
        return page?.Value
                   .Select(MapMessage)
                   .OrderByDescending(message => message.ReceivedOn)
                   .ToList()
               ?? [];
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
        request.Headers.TryAddWithoutValidation("Prefer", "outlook.body-content-type=\"text\"");
        return await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static EmailMessage MapMessage(GraphMessage message)
    {
        var body = message.Body?.Content ?? string.Empty;
        var preview = message.BodyPreview ?? string.Empty;
        return new EmailMessage
        {
            Id = message.Id ?? string.Empty,
            From = FormatSender(message.From?.EmailAddress),
            Subject = string.IsNullOrWhiteSpace(message.Subject) ? "(No subject)" : message.Subject,
            ReceivedOn = message.ReceivedDateTime?.LocalDateTime ?? DateTime.Now,
            Preview = string.IsNullOrWhiteSpace(preview) ? Truncate(body, 180) : preview,
            Body = string.IsNullOrWhiteSpace(body) ? preview : body
        };
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

        public DateTimeOffset? ReceivedDateTime { get; set; }

        public string? BodyPreview { get; set; }

        public GraphBody? Body { get; set; }
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
    }
}
