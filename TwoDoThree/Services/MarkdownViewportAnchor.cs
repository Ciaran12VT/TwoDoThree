namespace TwoDoThree.Services;

// Fractional source line plus its relative viewport position. Edges clamp naturally
// when one representation is shorter than the other.
public sealed record MarkdownViewportAnchor(double line, double viewport = 0, string? edge = null);
