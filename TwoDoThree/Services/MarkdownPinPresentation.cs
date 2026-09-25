using TwoDoThree.Models;

namespace TwoDoThree.Services;

public sealed record MarkdownPinPresentation(List<MarkdownPin> Pins, bool CanPin, bool Open = false, double Scroll = 0, string? Error = null,
    double Width = 330, bool Wrap = true);
