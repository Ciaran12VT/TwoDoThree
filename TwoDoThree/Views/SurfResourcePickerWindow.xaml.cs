using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using TwoDoThree.Models;

namespace TwoDoThree.Views;

public partial class SurfResourcePickerWindow : Window
{
    private readonly ICollectionView resourceView;

    public SurfResourcePickerWindow(IEnumerable<Surf2ResourceCandidate> resources)
    {
        InitializeComponent();
        DataContext = resources.ToList();
        resourceView = CollectionViewSource.GetDefaultView(DataContext);
        resourceView.Filter = FilterResource;
        Loaded += (_, _) =>
        {
            SearchTextBox.Focus();
            ResourceListBox.SelectedIndex = ResourceListBox.Items.Count > 0 ? 0 : -1;
        };
    }

    public Surf2ResourceCandidate? SelectedResource { get; private set; }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        resourceView.Refresh();
        ResourceListBox.SelectedIndex = ResourceListBox.Items.Count > 0 ? 0 : -1;
    }

    private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && ResourceListBox.Items.Count > 0)
        {
            ResourceListBox.Focus();
            ResourceListBox.SelectedIndex = Math.Max(0, ResourceListBox.SelectedIndex);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            CommitSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    private void ResourceListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    private void ResourceListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        CommitSelection();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        CommitSelection();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private bool FilterResource(object item)
    {
        if (item is not Surf2ResourceCandidate resource)
        {
            return false;
        }

        string filter = SearchTextBox.Text.Trim();
        return string.IsNullOrWhiteSpace(filter) ||
               resource.SearchText.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void CommitSelection()
    {
        if (ResourceListBox.SelectedItem is not Surf2ResourceCandidate resource)
        {
            return;
        }

        SelectedResource = resource;
        DialogResult = true;
    }
}
