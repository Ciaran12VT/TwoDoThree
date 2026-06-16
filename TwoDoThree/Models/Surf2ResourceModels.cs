using System.Text.Json;

namespace TwoDoThree.Models;

public sealed class Surf2ScopeOption
{
    public string ScopeId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? ScopeId : Name;

    public override string ToString() => DisplayName;
}

public sealed class Surf2ResourceCandidate
{
    public string ScopeId { get; set; } = string.Empty;

    public string ScopeName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string DisplaySummary => string.IsNullOrWhiteSpace(Type) ? Path : $"{Type} - {Path}";

    public string SearchText => $"{Name} {Type} {Path}";
}

public sealed class SurfResourceLink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public int Version { get; set; } = 1;

    public string ScopeId { get; set; } = string.Empty;

    public string ScopeName { get; set; } = string.Empty;

    public string ResourceName { get; set; } = string.Empty;

    public string ResourceKind { get; set; } = string.Empty;

    public string ResourceType { get; set; } = string.Empty;

    public string ResourcePath { get; set; } = string.Empty;

    public int? LineNumber { get; set; }

    public int? ColumnNumber { get; set; }

    public string DisplaySummary =>
        string.IsNullOrWhiteSpace(ResourceType)
            ? ResourcePath
            : $"{ResourceType} - {ResourcePath}";

    public bool Matches(Surf2ResourceCandidate candidate)
    {
        return string.Equals(ScopeId, candidate.ScopeId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(ResourcePath, candidate.Path, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(ResourceKind, candidate.Kind, StringComparison.OrdinalIgnoreCase);
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public static SurfResourceLink FromCandidate(Surf2ResourceCandidate candidate)
    {
        return new SurfResourceLink
        {
            ScopeId = candidate.ScopeId,
            ScopeName = candidate.ScopeName,
            ResourceName = candidate.Name,
            ResourceKind = candidate.Kind,
            ResourceType = candidate.Type,
            ResourcePath = candidate.Path
        };
    }

    public static bool TryParse(string? content, out SurfResourceLink link)
    {
        link = new SurfResourceLink();
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        try
        {
            SurfResourceLink? parsed = JsonSerializer.Deserialize<SurfResourceLink>(content, JsonOptions);
            if (parsed == null || string.IsNullOrWhiteSpace(parsed.ResourcePath))
            {
                return false;
            }

            link = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
