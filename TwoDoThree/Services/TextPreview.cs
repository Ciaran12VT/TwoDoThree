namespace TwoDoThree.Services;

public static class TextPreview
{
    public static string Create(string text, int maxLength = 180)
    {
        var normalized = string.Join(" ", text.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..maxLength].TrimEnd() + "...";
    }
}
