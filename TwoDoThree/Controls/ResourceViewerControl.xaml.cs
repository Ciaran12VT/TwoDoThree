using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TwoDoThree.Models;
using TwoDoThree.Views;

namespace TwoDoThree.Controls;

public partial class ResourceViewerControl : UserControl
{
    public static readonly DependencyProperty ResourceProperty =
        DependencyProperty.Register(
            nameof(Resource),
            typeof(ResourceItem),
            typeof(ResourceViewerControl),
            new PropertyMetadata(null));

    public ResourceViewerControl()
    {
        InitializeComponent();
    }

    public ResourceItem? Resource
    {
        get => (ResourceItem?)GetValue(ResourceProperty);
        set => SetValue(ResourceProperty, value);
    }

    public ResourceLinkRichTextBox? GetTextEditor()
    {
        return FindVisualChild<ResourceLinkRichTextBox>(this);
    }

    private void ImageResource_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2
            || sender is not FrameworkElement { DataContext: ResourceItem { Kind: ResourceKind.Image } resource })
        {
            return;
        }

        var window = new ImagePreviewWindow(resource)
        {
            Owner = Window.GetWindow(this)
        };

        window.Show();
        e.Handled = true;
    }

    private void SurfResource_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2 ||
            sender is not FrameworkElement { DataContext: ResourceItem { Kind: ResourceKind.SurfResource } resource })
        {
            return;
        }

        ResourceLinkHelper.FindTaskDetailViewModel(this)?.OpenLinkedResource(resource);
        e.Handled = true;
    }

    private void OpenSurfResourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ResourceItem { Kind: ResourceKind.SurfResource } resource })
        {
            ResourceLinkHelper.FindTaskDetailViewModel(this)?.OpenLinkedResource(resource);
            e.Handled = true;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}
