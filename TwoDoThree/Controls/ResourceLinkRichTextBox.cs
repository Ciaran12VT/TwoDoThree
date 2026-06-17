using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using TwoDoThree.Models;

namespace TwoDoThree.Controls;

public sealed class ResourceLinkRichTextBox : RichTextBox
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(ResourceLinkRichTextBox),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnTextPropertyChanged));

    public static readonly DependencyProperty FormattedTextProperty =
        DependencyProperty.Register(
            nameof(FormattedText),
            typeof(string),
            typeof(ResourceLinkRichTextBox),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnFormattedTextPropertyChanged));

    private static readonly SolidColorBrush LinkBrush = new(Color.FromRgb(37, 99, 235));

    private bool isApplyingFormat;
    private bool isUpdatingDocument;
    private bool isUpdatingFormattedText;
    private bool isUpdatingText;
    private PostResourceTypingStyleReset? pendingPostResourceTypingStyleReset;

    public ResourceLinkRichTextBox()
    {
        AllowDrop = true;
        AcceptsReturn = true;
        TextChanged += ResourceLinkRichTextBox_TextChanged;
        PreviewDragOver += ResourceLinkRichTextBox_PreviewDragOver;
        Drop += ResourceLinkRichTextBox_Drop;
        MouseDoubleClick += ResourceLinkRichTextBox_MouseDoubleClick;
        PreviewKeyDown += ResourceLinkRichTextBox_PreviewKeyDown;
        LostKeyboardFocus += (_, _) => pendingPostResourceTypingStyleReset = null;
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string FormattedText
    {
        get => (string)GetValue(FormattedTextProperty);
        set => SetValue(FormattedTextProperty, value);
    }

    public int CaretCharacterIndex => GetCharOffset(CaretPosition);

    public bool IsCaretOnFirstLine()
    {
        var textBeforeCaret = Text[..Math.Clamp(CaretCharacterIndex, 0, Text.Length)];
        return !textBeforeCaret.Contains('\n', StringComparison.Ordinal);
    }

    public bool IsCaretOnLastLine()
    {
        var offset = Math.Clamp(CaretCharacterIndex, 0, Text.Length);
        return !Text[offset..].Contains('\n', StringComparison.Ordinal);
    }

    public void FocusText(bool moveToEnd)
    {
        Focus();
        CaretPosition = GetTextPointerAtCharOffset(moveToEnd ? Text.Length : 0) ?? Document.ContentStart;
    }

    public void InsertResourceToken(ResourceItem resource)
    {
        TypingStyleSnapshot typingStyle = TypingStyleSnapshot.Capture(Selection, Foreground);
        string token = ResourceLinkHelper.CreateToken(resource);
        int insertionOffset = CaretCharacterIndex;
        Selection.Text = token;

        TextPointer? afterToken = GetTextPointerAtCharOffset(insertionOffset + token.Length);
        if (afterToken is not null)
        {
            CaretPosition = afterToken.GetInsertionPosition(LogicalDirection.Forward) ?? afterToken;
        }

        int afterTokenOffset = GetCharOffset(CaretPosition);
        ApplyResourceLinkFormatting();
        pendingPostResourceTypingStyleReset = new PostResourceTypingStyleReset(afterTokenOffset, typingStyle);
        RestoreTypingStyleAfterResourceLink(typingStyle);
        UpdateBoundTextAndFormatting();
    }

    public void SetSelectionFontFamily(string fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            return;
        }

        ApplySelectionProperty(TextElement.FontFamilyProperty, new FontFamily(fontFamily));
    }

    public void SetSelectionFontSize(double fontSize)
    {
        if (fontSize <= 0)
        {
            return;
        }

        ApplySelectionProperty(TextElement.FontSizeProperty, fontSize);
    }

    public void ToggleBold()
    {
        var value = Selection.GetPropertyValue(TextElement.FontWeightProperty);
        var nextValue = value is FontWeight fontWeight && fontWeight == FontWeights.Bold
            ? FontWeights.Normal
            : FontWeights.Bold;

        ApplySelectionProperty(TextElement.FontWeightProperty, nextValue);
    }

    public void ToggleItalic()
    {
        var value = Selection.GetPropertyValue(TextElement.FontStyleProperty);
        var nextValue = value is FontStyle fontStyle && fontStyle == FontStyles.Italic
            ? FontStyles.Normal
            : FontStyles.Italic;

        ApplySelectionProperty(TextElement.FontStyleProperty, nextValue);
    }

    public void ToggleUnderline()
    {
        ToggleTextDecoration(TextDecorationLocation.Underline);
    }

    public void ToggleStrikethrough()
    {
        ToggleTextDecoration(TextDecorationLocation.Strikethrough);
    }

    private static void OnTextPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var editor = (ResourceLinkRichTextBox)dependencyObject;
        if (editor.isUpdatingText)
        {
            return;
        }

        editor.SetDocumentText(e.NewValue as string ?? string.Empty);
    }

    private static void OnFormattedTextPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var editor = (ResourceLinkRichTextBox)dependencyObject;
        if (editor.isUpdatingFormattedText)
        {
            return;
        }

        var formattedText = e.NewValue as string ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(formattedText))
        {
            editor.SetDocumentFormattedText(formattedText);
        }
    }

    private void SetDocumentText(string text)
    {
        isUpdatingDocument = true;

        try
        {
            var range = new TextRange(Document.ContentStart, Document.ContentEnd)
            {
                Text = text
            };
            ApplyResourceLinkFormatting();
            UpdateBoundTextAndFormatting();
        }
        finally
        {
            isUpdatingDocument = false;
        }
    }

    private void SetDocumentFormattedText(string formattedText)
    {
        isUpdatingDocument = true;

        try
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(formattedText);
            using var stream = new MemoryStream(bytes);
            new TextRange(Document.ContentStart, Document.ContentEnd).Load(stream, DataFormats.Xaml);
            ApplyResourceLinkFormatting();
            UpdateBoundTextAndFormatting();
        }
        catch (Exception)
        {
            SetDocumentText(Text);
        }
        finally
        {
            isUpdatingDocument = false;
        }
    }

    private void ResourceLinkRichTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (isUpdatingDocument || isApplyingFormat)
        {
            return;
        }

        TextPointer caretPosition = CaretPosition;
        int caretOffset = GetCharOffset(caretPosition);
        UpdateBoundPlainText();
        ApplyResourceLinkFormatting();
        ApplyPendingPostResourceTypingStyleReset(caretOffset);
        UpdateBoundFormattedText();

        CaretPosition = caretPosition.GetInsertionPosition(LogicalDirection.Forward) ?? Document.ContentEnd;
        RestorePendingTypingStyleAtCaret(GetCharOffset(CaretPosition));
    }

    private void ResourceLinkRichTextBox_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = ResourceLinkHelper.TryGetDraggedResource(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ResourceLinkRichTextBox_Drop(object sender, DragEventArgs e)
    {
        if (!ResourceLinkHelper.TryGetDraggedResource(e, out var resource) || resource is null)
        {
            return;
        }

        var position = GetPositionFromPoint(e.GetPosition(this), snapToText: true);
        if (position is not null)
        {
            CaretPosition = position;
        }

        InsertResourceToken(resource);
        e.Handled = true;
    }

    private void ResourceLinkRichTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var resourceName = ResourceLinkHelper.GetResourceNameAtOffset(Text, CaretCharacterIndex);
        if (ResourceLinkHelper.TryOpenResource(resourceName, this))
        {
            e.Handled = true;
        }
    }

    private async void ResourceLinkRichTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.I ||
            (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != (ModifierKeys.Control | ModifierKeys.Alt))
        {
            return;
        }

        e.Handled = true;
        if (ResourceLinkHelper.FindTaskDetailViewModel(this) is not { } viewModel)
        {
            return;
        }

        IReadOnlyList<Surf2ResourceCandidate> resources = await viewModel.GetSurfResourcesForInsertionAsync();
        SurfResourceIntellisensePopup.Open(this, resources, candidate =>
        {
            ResourceItem resource = viewModel.GetOrCreateSurfResource(candidate);
            Focus();
            InsertResourceToken(resource);
        });
    }

    private string GetDocumentText()
    {
        var text = new TextRange(Document.ContentStart, Document.ContentEnd).Text;
        return text.EndsWith("\r\n", StringComparison.Ordinal)
            ? text[..^2]
            : text;
    }

    private string GetDocumentFormattedText()
    {
        var range = new TextRange(Document.ContentStart, Document.ContentEnd);
        using var stream = new MemoryStream();
        range.Save(stream, DataFormats.Xaml);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private void UpdateBoundTextAndFormatting()
    {
        UpdateBoundPlainText();
        UpdateBoundFormattedText();
    }

    private void UpdateBoundPlainText()
    {
        isUpdatingText = true;

        try
        {
            Text = GetDocumentText();
        }
        finally
        {
            isUpdatingText = false;
        }
    }

    private void UpdateBoundFormattedText()
    {
        isUpdatingFormattedText = true;

        try
        {
            FormattedText = GetDocumentFormattedText();
        }
        finally
        {
            isUpdatingFormattedText = false;
        }
    }

    private void ApplySelectionProperty(DependencyProperty property, object value)
    {
        Focus();
        Selection.ApplyPropertyValue(property, value);
        ApplyResourceLinkFormatting();
        UpdateBoundTextAndFormatting();
    }

    private void RestoreTypingStyleAfterResourceLink(TypingStyleSnapshot typingStyle)
    {
        typingStyle.ApplyTo(Selection);
    }

    private void ApplyPendingPostResourceTypingStyleReset(int caretOffset)
    {
        if (pendingPostResourceTypingStyleReset is not { } reset ||
            caretOffset <= reset.StartOffset)
        {
            return;
        }

        TextPointer? start = GetTextPointerAtCharOffset(reset.StartOffset);
        TextPointer? end = GetTextPointerAtCharOffset(caretOffset);
        if (start is null || end is null || start.CompareTo(end) >= 0)
        {
            return;
        }

        isApplyingFormat = true;
        try
        {
            reset.TypingStyle.ApplyTo(new TextRange(start, end));
            reset.EndOffset = Math.Max(reset.EndOffset, caretOffset);
        }
        finally
        {
            isApplyingFormat = false;
        }
    }

    private void RestorePendingTypingStyleAtCaret(int caretOffset)
    {
        if (pendingPostResourceTypingStyleReset is not { } reset)
        {
            return;
        }

        if (caretOffset < reset.StartOffset || caretOffset > reset.EndOffset)
        {
            pendingPostResourceTypingStyleReset = null;
            return;
        }

        reset.TypingStyle.ApplyTo(Selection);
    }

    private void ToggleTextDecoration(TextDecorationLocation location)
    {
        var value = Selection.GetPropertyValue(Inline.TextDecorationsProperty);
        var decorations = value is TextDecorationCollection existing
            ? existing.Clone()
            : new TextDecorationCollection();

        var matchingDecorations = decorations
            .Where(decoration => decoration.Location == location)
            .ToList();

        if (matchingDecorations.Count > 0)
        {
            foreach (var decoration in matchingDecorations)
            {
                decorations.Remove(decoration);
            }
        }
        else
        {
            var source = location == TextDecorationLocation.Underline
                ? TextDecorations.Underline
                : TextDecorations.Strikethrough;
            decorations.Add(source.First().Clone());
        }

        ApplySelectionProperty(
            Inline.TextDecorationsProperty,
            decorations);
    }

    private void ApplyResourceLinkFormatting()
    {
        isApplyingFormat = true;

        try
        {
            string documentText = BuildDocumentTextMap(out List<TextCharacterRange> characterRanges);
            foreach (var link in ResourceLinkHelper.GetResourceLinks(documentText))
            {
                int endIndex = link.Index + link.Length - 1;
                if (link.Index < 0 ||
                    endIndex < link.Index ||
                    endIndex >= characterRanges.Count)
                {
                    continue;
                }

                var linkRange = new TextRange(
                    characterRanges[link.Index].Start,
                    characterRanges[endIndex].End);
                linkRange.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
                linkRange.ApplyPropertyValue(TextElement.ForegroundProperty, LinkBrush);
            }
        }
        finally
        {
            isApplyingFormat = false;
        }
    }

    private string BuildDocumentTextMap(out List<TextCharacterRange> characterRanges)
    {
        var text = new StringBuilder();
        characterRanges = [];

        TextPointer? current = Document.ContentStart;
        while (current is not null && current.CompareTo(Document.ContentEnd) < 0)
        {
            if (current.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                string runText = current.GetTextInRun(LogicalDirection.Forward);
                for (int index = 0; index < runText.Length; index++)
                {
                    TextPointer? start = current.GetPositionAtOffset(index);
                    TextPointer? end = current.GetPositionAtOffset(index + 1);
                    if (start is null || end is null)
                    {
                        continue;
                    }

                    text.Append(runText[index]);
                    characterRanges.Add(new TextCharacterRange(start, end));
                }
            }

            current = current.GetNextContextPosition(LogicalDirection.Forward);
        }

        return text.ToString();
    }

    private TextPointer? GetTextPointerAtCharOffset(int charOffset)
    {
        charOffset = Math.Max(0, charOffset);
        var current = Document.ContentStart;
        var currentOffset = 0;

        while (current is not null && current.CompareTo(Document.ContentEnd) < 0)
        {
            if (current.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var runText = current.GetTextInRun(LogicalDirection.Forward);
                if (currentOffset + runText.Length >= charOffset)
                {
                    return current.GetPositionAtOffset(charOffset - currentOffset);
                }

                currentOffset += runText.Length;
            }

            current = current.GetNextContextPosition(LogicalDirection.Forward);
        }

        return Document.ContentEnd;
    }

    private int GetCharOffset(TextPointer pointer)
    {
        var current = Document.ContentStart;
        var currentOffset = 0;

        while (current is not null && current.CompareTo(Document.ContentEnd) < 0)
        {
            if (current.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var runText = current.GetTextInRun(LogicalDirection.Forward);
                var next = current.GetPositionAtOffset(runText.Length);
                if (next is not null && pointer.CompareTo(next) <= 0)
                {
                    return currentOffset + Math.Max(0, current.GetOffsetToPosition(pointer));
                }

                currentOffset += runText.Length;
            }

            current = current.GetNextContextPosition(LogicalDirection.Forward);
        }

        return currentOffset;
    }

    private sealed class PostResourceTypingStyleReset
    {
        public PostResourceTypingStyleReset(
            int startOffset,
            TypingStyleSnapshot typingStyle)
        {
            StartOffset = startOffset;
            EndOffset = startOffset;
            TypingStyle = typingStyle;
        }

        public int StartOffset { get; }

        public int EndOffset { get; set; }

        public TypingStyleSnapshot TypingStyle { get; }
    }

    private readonly record struct TextCharacterRange(
        TextPointer Start,
        TextPointer End);

    private sealed class TypingStyleSnapshot
    {
        private TypingStyleSnapshot(
            object fontWeight,
            object foreground,
            object fontStyle,
            object fontFamily,
            object fontSize,
            object textDecorations)
        {
            FontWeight = fontWeight;
            Foreground = foreground;
            FontStyle = fontStyle;
            FontFamily = fontFamily;
            FontSize = fontSize;
            TextDecorations = textDecorations;
        }

        private object FontWeight { get; }

        private object Foreground { get; }

        private object FontStyle { get; }

        private object FontFamily { get; }

        private object FontSize { get; }

        private object TextDecorations { get; }

        public static TypingStyleSnapshot Capture(TextSelection selection, Brush fallbackForeground)
        {
            return new TypingStyleSnapshot(
                CaptureProperty(selection, TextElement.FontWeightProperty, FontWeights.Normal),
                CaptureProperty(selection, TextElement.ForegroundProperty, fallbackForeground),
                CaptureProperty(selection, TextElement.FontStyleProperty, FontStyles.Normal),
                CaptureProperty(selection, TextElement.FontFamilyProperty, SystemFonts.MessageFontFamily),
                CaptureProperty(selection, TextElement.FontSizeProperty, SystemFonts.MessageFontSize),
                CaptureTextDecorations(selection));
        }

        public void ApplyTo(TextSelection selection)
        {
            ApplyToRange(selection);
        }

        public void ApplyTo(TextRange range)
        {
            ApplyToRange(range);
        }

        private void ApplyToRange(TextRange range)
        {
            range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeight);
            range.ApplyPropertyValue(TextElement.ForegroundProperty, Foreground);
            range.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyle);
            range.ApplyPropertyValue(TextElement.FontFamilyProperty, FontFamily);
            range.ApplyPropertyValue(TextElement.FontSizeProperty, FontSize);
            range.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations);
        }

        private static object CaptureProperty(
            TextSelection selection,
            DependencyProperty property,
            object fallback)
        {
            object value = selection.GetPropertyValue(property);
            return value == DependencyProperty.UnsetValue ? fallback : value;
        }

        private static object CaptureTextDecorations(TextSelection selection)
        {
            object value = selection.GetPropertyValue(Inline.TextDecorationsProperty);
            return value is TextDecorationCollection decorations
                ? decorations.Clone()
                : new TextDecorationCollection();
        }
    }
}
