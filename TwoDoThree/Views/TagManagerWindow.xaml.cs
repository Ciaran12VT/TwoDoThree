using System.Windows;
using TwoDoThree.Models;
using TwoDoThree.ViewModels;

namespace TwoDoThree.Views;

public partial class TagManagerWindow : Window
{
    private readonly TagSettings tagSettings;

    public TagManagerWindow(TagSettings tagSettings, string? suggestedTag = null)
    {
        InitializeComponent();
        this.tagSettings = tagSettings;
        DataContext = new TagManagerViewModel(tagSettings.Tags, suggestedTag);
        Loaded += (_, _) =>
        {
            NewTagTextBox.Focus();
            NewTagTextBox.SelectAll();
        };
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is TagManagerViewModel viewModel)
        {
            viewModel.ApplyTo(tagSettings);
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
