using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using TwoDoThree.Models;

namespace TwoDoThree.Controls;

public partial class CodeSnippetEditor : UserControl
{
    private static readonly SolidColorBrush LinkBrush = new(Color.FromRgb(37, 99, 235));

    public static readonly DependencyProperty ResourceProperty =
        DependencyProperty.Register(
            nameof(Resource),
            typeof(ResourceItem),
            typeof(CodeSnippetEditor),
            new PropertyMetadata(null, OnResourceChanged));

    private bool isUpdatingEditor;
    private bool isUpdatingResource;

    public CodeSnippetEditor()
    {
        InitializeComponent();
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 4;
        Editor.TextArea.TextView.LineTransformers.Add(new ResourceLinkColorizer());
    }

    public ResourceItem? Resource
    {
        get => (ResourceItem?)GetValue(ResourceProperty);
        set => SetValue(ResourceProperty, value);
    }

    private static void OnResourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var editor = (CodeSnippetEditor)dependencyObject;

        if (e.OldValue is ResourceItem oldResource)
        {
            oldResource.PropertyChanged -= editor.Resource_PropertyChanged;
        }

        if (e.NewValue is ResourceItem newResource)
        {
            newResource.PropertyChanged += editor.Resource_PropertyChanged;
        }

        editor.RefreshEditorFromResource();
    }

    private void Resource_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isUpdatingEditor || isUpdatingResource)
        {
            return;
        }

        if (e.PropertyName == nameof(ResourceItem.Content))
        {
            Dispatcher.Invoke(() =>
            {
                if (Resource is null || Editor.Text == Resource.Content)
                {
                    return;
                }

                isUpdatingEditor = true;
                try
                {
                    Editor.Text = Resource.Content;
                }
                finally
                {
                    isUpdatingEditor = false;
                }
            });
        }
        else if (e.PropertyName == nameof(ResourceItem.CodeLanguage))
        {
            Dispatcher.Invoke(() => ApplySyntaxHighlighting(Resource?.CodeLanguage));
        }
    }

    private void RefreshEditorFromResource()
    {
        isUpdatingEditor = true;

        try
        {
            Editor.Text = Resource?.Content ?? string.Empty;
            ApplySyntaxHighlighting(Resource?.CodeLanguage);
        }
        finally
        {
            isUpdatingEditor = false;
        }
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (isUpdatingEditor || Resource is null)
        {
            return;
        }

        isUpdatingResource = true;
        try
        {
            Resource.Content = Editor.Text;
        }
        finally
        {
            isUpdatingResource = false;
        }
    }

    private void Editor_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = ResourceLinkHelper.TryGetDraggedResource(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Editor_Drop(object sender, DragEventArgs e)
    {
        if (!ResourceLinkHelper.TryGetDraggedResource(e, out var resource) || resource is null)
        {
            return;
        }

        var position = Editor.GetPositionFromPoint(e.GetPosition(Editor));
        if (position.HasValue)
        {
            Editor.CaretOffset = Editor.Document.GetOffset(position.Value.Location);
        }

        Editor.Document.Insert(Editor.CaretOffset, ResourceLinkHelper.CreateToken(resource));
        e.Handled = true;
    }

    private void Editor_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var resourceName = ResourceLinkHelper.GetResourceNameAtOffset(Editor.Text, Editor.CaretOffset);
        if (ResourceLinkHelper.TryOpenResource(resourceName, this))
        {
            e.Handled = true;
        }
    }

    private async void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
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
        SurfResourceIntellisensePopup.Open(Editor, resources, candidate =>
        {
            ResourceItem resource = viewModel.GetOrCreateSurfResource(candidate);
            Editor.Focus();
            Editor.Document.Insert(Editor.CaretOffset, ResourceLinkHelper.CreateToken(resource));
        });
    }

    private void LanguageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || Resource is null)
        {
            return;
        }

        var language = item.Tag as string ?? string.Empty;
        Resource.CodeLanguage = language;
        ApplySyntaxHighlighting(language);
    }

    private void ApplySyntaxHighlighting(string? language)
    {
        Editor.SyntaxHighlighting = string.IsNullOrWhiteSpace(language)
            ? null
            : HighlightingManager.Instance.GetDefinition(language);
    }

    private sealed class ResourceLinkColorizer : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine line)
        {
            var lineText = CurrentContext.Document.GetText(line);

            foreach (var link in ResourceLinkHelper.GetResourceLinks(lineText))
            {
                ChangeLinePart(
                    line.Offset + link.Index,
                    line.Offset + link.Index + link.Length,
                    element =>
                    {
                        element.TextRunProperties.SetForegroundBrush(LinkBrush);
                        element.TextRunProperties.SetTypeface(new Typeface(
                            element.TextRunProperties.Typeface.FontFamily,
                            element.TextRunProperties.Typeface.Style,
                            FontWeights.Bold,
                            element.TextRunProperties.Typeface.Stretch));
                    });
            }
        }
    }
}
