using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TwoDoThree.Controls;

public partial class TagEditorControl : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(TagEditorControl),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnTextChanged));

    public static readonly DependencyProperty AvailableTagsProperty =
        DependencyProperty.Register(
            nameof(AvailableTags),
            typeof(IEnumerable),
            typeof(TagEditorControl),
            new PropertyMetadata(null, OnAvailableTagsChanged));

    private bool isUpdatingText;

    public TagEditorControl()
    {
        InitializeComponent();
    }

    public event EventHandler? ManageTagsRequested;

    public ObservableCollection<string> FilteredTags { get; } = new();

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IEnumerable? AvailableTags
    {
        get => (IEnumerable?)GetValue(AvailableTagsProperty);
        set => SetValue(AvailableTagsProperty, value);
    }

    public string CurrentToken => GetCurrentToken();

    public void ApplyTag(string tag)
    {
        if (!string.IsNullOrWhiteSpace(tag))
        {
            ApplySuggestion(tag.Trim());
        }
    }

    private static void OnTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var editor = (TagEditorControl)dependencyObject;
        if (!editor.isUpdatingText)
        {
            editor.UpdateSuggestions();
        }
    }

    private static void OnAvailableTagsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var editor = (TagEditorControl)dependencyObject;

        if (e.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= editor.AvailableTags_CollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += editor.AvailableTags_CollectionChanged;
        }

        editor.UpdateSuggestions();
    }

    private void AvailableTags_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateSuggestions();
    }

    private void TagsTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSuggestions();
    }

    private void TagsTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        UpdateSuggestions();
    }

    private void TagsTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (SuggestionsPopup.IsKeyboardFocusWithin || IsFocusInsideEditor(e.NewFocus))
        {
            return;
        }

        SuggestionsPopup.IsOpen = false;
        NormalizeToAvailableTags();
    }

    private void TagsTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SuggestionsPopup.IsOpen = false;
            return;
        }

        if ((e.Key == Key.Enter || e.Key == Key.Tab)
            && SuggestionsPopup.IsOpen
            && (SuggestionsListBox.SelectedItem as string ?? FilteredTags.FirstOrDefault()) is { } tag)
        {
            ApplySuggestion(tag);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down && SuggestionsPopup.IsOpen && FilteredTags.Count > 0)
        {
            SuggestionsListBox.Focus();
            SuggestionsListBox.SelectedIndex = 0;
            e.Handled = true;
        }
    }

    private void SuggestionsListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (SuggestionsListBox.SelectedItem is string tag)
        {
            ApplySuggestion(tag);
            e.Handled = true;
        }
    }

    private void ManageTagsButton_Click(object sender, RoutedEventArgs e)
    {
        ManageTagsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSuggestions()
    {
        if (TagsTextBox is null)
        {
            return;
        }

        var token = GetCurrentToken();
        var selectedTags = ParseTags(Text)
            .Where(tag => !string.Equals(tag, token, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var suggestions = GetAvailableTagValues()
            .Where(tag => !selectedTags.Contains(tag))
            .Where(tag => string.IsNullOrWhiteSpace(token)
                          || tag.Contains(token, StringComparison.OrdinalIgnoreCase))
            .Take(12)
            .ToList();

        FilteredTags.Clear();
        foreach (var suggestion in suggestions)
        {
            FilteredTags.Add(suggestion);
        }

        SuggestionsPopup.IsOpen = TagsTextBox.IsKeyboardFocusWithin && FilteredTags.Count > 0;
    }

    private void ApplySuggestion(string tag)
    {
        var (start, length) = GetCurrentTokenBounds();
        var prefix = Text[..start].TrimEnd();
        var suffix = Text[(start + length)..].TrimStart();
        var replacement = tag;

        var nextText = string.IsNullOrWhiteSpace(prefix)
            ? replacement
            : $"{prefix.TrimEnd(',', ' ')}, {replacement}";

        if (!string.IsNullOrWhiteSpace(suffix.Trim(',', ' ')))
        {
            nextText = $"{nextText}, {suffix.TrimStart(',', ' ')}";
        }

        isUpdatingText = true;
        Text = nextText;
        TagsTextBox.Text = nextText;
        isUpdatingText = false;

        TagsTextBox.CaretIndex = Math.Min(nextText.Length, nextText.IndexOf(replacement, StringComparison.Ordinal) + replacement.Length);
        SuggestionsPopup.IsOpen = false;
        TagsTextBox.Focus();
    }

    private void NormalizeToAvailableTags()
    {
        var availableTags = GetAvailableTagValues().ToList();
        if (availableTags.Count == 0)
        {
            return;
        }

        var normalizedTags = ParseTags(Text)
            .Select(tag => availableTags.FirstOrDefault(availableTag =>
                string.Equals(availableTag, tag, StringComparison.OrdinalIgnoreCase)))
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var normalizedText = string.Join(", ", normalizedTags);
        if (string.Equals(Text, normalizedText, StringComparison.Ordinal))
        {
            return;
        }

        isUpdatingText = true;
        Text = normalizedText;
        TagsTextBox.Text = normalizedText;
        isUpdatingText = false;
    }

    private string GetCurrentToken()
    {
        var (start, length) = GetCurrentTokenBounds();
        return Text.Substring(start, length).Trim();
    }

    private (int Start, int Length) GetCurrentTokenBounds()
    {
        var text = Text ?? string.Empty;
        if (text.Length == 0)
        {
            return (0, 0);
        }

        var caretIndex = TagsTextBox is null
            ? text.Length
            : Math.Clamp(TagsTextBox.CaretIndex, 0, text.Length);
        var start = caretIndex == 0
            ? -1
            : text.LastIndexOf(',', caretIndex - 1);
        start = start < 0 ? 0 : start + 1;
        var end = text.IndexOf(',', caretIndex);
        end = end < 0 ? text.Length : end;

        while (start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        return (start, end - start);
    }

    private IEnumerable<string> GetAvailableTagValues()
    {
        return AvailableTags?.Cast<object>()
                   .Select(tag => tag.ToString()?.Trim() ?? string.Empty)
                   .Where(tag => !string.IsNullOrWhiteSpace(tag))
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
               ?? Enumerable.Empty<string>();
    }

    private static IEnumerable<string> ParseTags(string? text)
    {
        return (text ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private bool IsFocusInsideEditor(IInputElement? focus)
    {
        var current = focus as DependencyObject;
        while (current is not null)
        {
            if (ReferenceEquals(current, this))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }

        return false;
    }
}
