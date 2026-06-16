using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

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

    public ResourceLinkRichTextBox()
    {
        AllowDrop = true;
        AcceptsReturn = true;
        TextChanged += ResourceLinkRichTextBox_TextChanged;
        PreviewDragOver += ResourceLinkRichTextBox_PreviewDragOver;
        Drop += ResourceLinkRichTextBox_Drop;
        MouseDoubleClick += ResourceLinkRichTextBox_MouseDoubleClick;
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

        var caretPosition = CaretPosition;
        UpdateBoundPlainText();
        ApplyResourceLinkFormatting();
        UpdateBoundFormattedText();
        CaretPosition = caretPosition.GetInsertionPosition(LogicalDirection.Forward) ?? Document.ContentEnd;
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

        Selection.Text = ResourceLinkHelper.CreateToken(resource);
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
            foreach (var link in ResourceLinkHelper.GetResourceLinks(Text))
            {
                var start = GetTextPointerAtCharOffset(link.Index);
                var end = GetTextPointerAtCharOffset(link.Index + link.Length);
                if (start is null || end is null)
                {
                    continue;
                }

                var linkRange = new TextRange(start, end);
                linkRange.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
                linkRange.ApplyPropertyValue(TextElement.ForegroundProperty, LinkBrush);
            }
        }
        finally
        {
            isApplyingFormat = false;
        }
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
}
