using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace TwoDoThree.Services;

public static class EmailBodyHtml
{
    private static readonly Regex BodyRegex = new(
        "<body\\b[^>]*>(?<body>.*?)</body>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex DangerousBlocksRegex = new(
        "<\\s*(script|iframe|object|embed|form|noscript|svg|math)\\b[^>]*>.*?<\\s*/\\s*\\1\\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex DangerousTagsRegex = new(
        "<\\s*/?\\s*(script|iframe|object|embed|form|input|button|textarea|select|option|meta|link|base|svg|math)\\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex EventAttributesRegex = new(
        "\\s+on\\w+\\s*=\\s*(\"[^\"]*\"|'[^']*'|[^\\s>]+)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex UrlAttributesRegex = new(
        "\\s(?<name>href|src)\\s*=\\s*(?<quote>[\"'])(?<value>.*?)\\k<quote>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ImageSourceRegex = new(
        "(?<prefix><img\\b[^>]*?\\s)src\\s*=\\s*(?<quote>[\"'])(?<src>.*?)\\k<quote>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex CidSourceRegex = new(
        "(?<prefix>\\ssrc\\s*=\\s*)(?<quote>[\"'])cid:(?<cid>.*?)\\k<quote>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex UrlRegex = new(
        "(https?://[^\\s<]+|mailto:[^\\s<]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string CreateDisplayDocument(string? htmlBody, string? plainText)
    {
        var body = string.IsNullOrWhiteSpace(htmlBody)
            ? LinkifyPlainText(plainText ?? string.Empty)
            : SanitizeHtmlFragment(htmlBody);

        return $$"""
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<style>
html, body {
    margin: 0;
    padding: 0;
    background: #ffffff;
    color: #111827;
    font-family: "Segoe UI", Arial, sans-serif;
    font-size: 14px;
    line-height: 1.45;
}
body {
    padding: 12px;
    box-sizing: border-box;
}
a {
    color: #2563eb;
    text-decoration: underline;
}
img {
    max-width: 100%;
    height: auto;
}
table {
    max-width: 100%;
    border-collapse: collapse;
}
pre {
    white-space: pre-wrap;
    word-break: break-word;
}
</style>
</head>
<body>{{body}}</body>
</html>
""";
    }

    public static string EmbedInlineImages(string htmlBody, IEnumerable<EmailInlineImage> images)
    {
        if (string.IsNullOrWhiteSpace(htmlBody))
        {
            return string.Empty;
        }

        var imageByContentId = images
            .Where(image => !string.IsNullOrWhiteSpace(image.ContentId)
                            && !string.IsNullOrWhiteSpace(image.Base64Content))
            .GroupBy(image => NormalizeContentId(image.ContentId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        if (imageByContentId.Count == 0)
        {
            return htmlBody;
        }

        return CidSourceRegex.Replace(htmlBody, match =>
        {
            var contentId = NormalizeContentId(WebUtility.HtmlDecode(match.Groups["cid"].Value));
            if (!imageByContentId.TryGetValue(contentId, out var image))
            {
                return match.Value;
            }

            var contentType = string.IsNullOrWhiteSpace(image.ContentType)
                ? "application/octet-stream"
                : image.ContentType;
            var dataUri = $"data:{contentType};base64,{image.Base64Content}";
            return $"{match.Groups["prefix"].Value}{match.Groups["quote"].Value}{dataUri}{match.Groups["quote"].Value}";
        });
    }

    public static string ToPlainText(string? htmlBody)
    {
        if (string.IsNullOrWhiteSpace(htmlBody))
        {
            return string.Empty;
        }

        var text = ExtractBodyFragment(htmlBody);
        text = Regex.Replace(text, "<\\s*br\\s*/?\\s*>", Environment.NewLine, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "</\\s*(p|div|li|tr|h[1-6])\\s*>", Environment.NewLine, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", string.Empty, RegexOptions.Singleline);
        text = WebUtility.HtmlDecode(text);
        return string.Join(
            Environment.NewLine,
            text.Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n')
                .Select(line => line.TrimEnd())
                .Where(line => !string.IsNullOrWhiteSpace(line))).Trim();
    }

    private static string SanitizeHtmlFragment(string html)
    {
        var fragment = ExtractBodyFragment(html);
        fragment = DangerousBlocksRegex.Replace(fragment, string.Empty);
        fragment = DangerousTagsRegex.Replace(fragment, string.Empty);
        fragment = EventAttributesRegex.Replace(fragment, string.Empty);
        fragment = UrlAttributesRegex.Replace(fragment, match =>
        {
            var value = WebUtility.HtmlDecode(match.Groups["value"].Value).Trim();
            if (IsAllowedUrl(value))
            {
                return match.Value;
            }

            return string.Empty;
        });
        fragment = ImageSourceRegex.Replace(fragment, match =>
        {
            var source = WebUtility.HtmlDecode(match.Groups["src"].Value).Trim();
            if (!IsRemoteUrl(source))
            {
                return match.Value;
            }

            return $"{match.Groups["prefix"].Value}data-remote-src={match.Groups["quote"].Value}{WebUtility.HtmlEncode(source)}{match.Groups["quote"].Value}";
        });

        return fragment;
    }

    private static string ExtractBodyFragment(string html)
    {
        var match = BodyRegex.Match(html);
        return match.Success ? match.Groups["body"].Value : html;
    }

    private static string LinkifyPlainText(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var lastIndex = 0;
        foreach (Match match in UrlRegex.Matches(plainText))
        {
            builder.Append(WebUtility.HtmlEncode(plainText[lastIndex..match.Index]));
            var url = match.Value.TrimEnd('.', ',', ';', ')');
            var trailing = match.Value[url.Length..];
            var encodedUrl = WebUtility.HtmlEncode(url);
            builder.Append(CanOpenExternalUrl(url)
                ? $"<a href=\"{encodedUrl}\">{encodedUrl}</a>"
                : encodedUrl);
            builder.Append(WebUtility.HtmlEncode(trailing));
            lastIndex = match.Index + match.Length;
        }

        builder.Append(WebUtility.HtmlEncode(plainText[lastIndex..]));
        return builder.ToString()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", "<br>");
    }

    private static bool IsAllowedUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.StartsWith('#'))
        {
            return true;
        }

        if (value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return CanOpenExternalUrl(value) || IsRemoteUrl(value);
    }

    private static bool CanOpenExternalUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && uri.Scheme is "http" or "https" or "mailto" or "tel";
    }

    private static bool IsRemoteUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && uri.Scheme is "http" or "https";
    }

    private static string NormalizeContentId(string contentId)
    {
        return contentId.Trim().Trim('<', '>').Trim();
    }
}
