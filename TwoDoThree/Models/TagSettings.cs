using System.Collections.ObjectModel;
using TwoDoThree.ViewModels;

namespace TwoDoThree.Models;

public sealed class TagSettings : ObservableObject
{
    public TagSettings()
    {
        ReplaceTags([
            "manual",
            "email",
            "outlook",
            "onboarding",
            "process",
            "planning",
            "review",
            "migration",
            "screenshot"
        ]);
    }

    public ObservableCollection<string> Tags { get; } = new();

    public void ReplaceTags(IEnumerable<string> tags)
    {
        Tags.Clear();

        foreach (var tag in NormalizeTags(tags))
        {
            Tags.Add(tag);
        }
    }

    public static IReadOnlyList<string> NormalizeTags(IEnumerable<string> tags)
    {
        return tags
            .Select(tag => tag.Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
