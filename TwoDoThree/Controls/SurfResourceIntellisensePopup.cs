using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using TwoDoThree.Models;

namespace TwoDoThree.Controls;

public sealed class SurfResourceIntellisensePopup
{
    private readonly Popup popup;
    private readonly TextBox searchBox;
    private readonly ListBox resourceList;
    private readonly ICollectionView resourceView;
    private readonly Action<Surf2ResourceCandidate> commit;

    private SurfResourceIntellisensePopup(
        FrameworkElement placementTarget,
        IReadOnlyList<Surf2ResourceCandidate> resources,
        Action<Surf2ResourceCandidate> commit)
    {
        this.commit = commit;
        searchBox = new TextBox
        {
            MinWidth = 280,
            Margin = new Thickness(8, 8, 8, 6)
        };
        resourceList = new ListBox
        {
            MaxHeight = 240,
            MinWidth = 360,
            Margin = new Thickness(8, 0, 8, 8),
            ItemsSource = resources
        };
        resourceList.ItemTemplate = CreateItemTemplate();

        resourceView = CollectionViewSource.GetDefaultView(resources);
        resourceView.Filter = FilterResource;

        var panel = new StackPanel();
        panel.Children.Add(searchBox);
        panel.Children.Add(resourceList);

        var border = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(156, 163, 175)),
            BorderThickness = new Thickness(1),
            Child = panel
        };

        popup = new Popup
        {
            PlacementTarget = placementTarget,
            Placement = PlacementMode.Relative,
            HorizontalOffset = 8,
            VerticalOffset = 24,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = border
        };

        searchBox.TextChanged += (_, _) => Refresh();
        searchBox.KeyDown += SearchBox_KeyDown;
        resourceList.KeyDown += ResourceList_KeyDown;
        resourceList.MouseDoubleClick += (_, _) => CommitSelection();
    }

    public static void Open(
        FrameworkElement placementTarget,
        IReadOnlyList<Surf2ResourceCandidate> resources,
        Action<Surf2ResourceCandidate> commit)
    {
        if (resources.Count == 0)
        {
            return;
        }

        var session = new SurfResourceIntellisensePopup(placementTarget, resources, commit);
        session.Show();
    }

    private void Show()
    {
        popup.IsOpen = true;
        Refresh();
        searchBox.Focus();
    }

    private void Refresh()
    {
        resourceView.Refresh();
        resourceList.SelectedIndex = resourceList.Items.Count > 0 ? 0 : -1;
    }

    private bool FilterResource(object item)
    {
        if (item is not Surf2ResourceCandidate resource)
        {
            return false;
        }

        string filter = searchBox.Text.Trim();
        return string.IsNullOrWhiteSpace(filter) ||
               resource.SearchText.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && resourceList.Items.Count > 0)
        {
            resourceList.Focus();
            resourceList.SelectedIndex = Math.Max(0, resourceList.SelectedIndex);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            CommitSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            popup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void ResourceList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            popup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void CommitSelection()
    {
        if (resourceList.SelectedItem is not Surf2ResourceCandidate resource)
        {
            return;
        }

        popup.IsOpen = false;
        commit(resource);
    }

    private static DataTemplate CreateItemTemplate()
    {
        var template = new DataTemplate(typeof(Surf2ResourceCandidate));

        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(FrameworkElement.MarginProperty, new Thickness(4));

        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(Surf2ResourceCandidate.Name)));
        name.SetValue(TextElement.FontWeightProperty, FontWeights.SemiBold);
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        panel.AppendChild(name);

        var summary = new FrameworkElementFactory(typeof(TextBlock));
        summary.SetBinding(TextBlock.TextProperty, new Binding(nameof(Surf2ResourceCandidate.DisplaySummary)));
        summary.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 0));
        summary.SetValue(TextElement.ForegroundProperty, new SolidColorBrush(Color.FromRgb(107, 114, 128)));
        summary.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        panel.AppendChild(summary);

        template.VisualTree = panel;
        return template;
    }
}
