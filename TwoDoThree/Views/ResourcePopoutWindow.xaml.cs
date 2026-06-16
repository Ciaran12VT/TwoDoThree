using System.ComponentModel;
using System.Windows;
using TwoDoThree.Models;
using TwoDoThree.ViewModels;

namespace TwoDoThree.Views;

public partial class ResourcePopoutWindow : Window
{
    public ResourcePopoutWindow(TaskDetailViewModel viewModel, ResourceItem resource)
    {
        Resource = resource;
        InitializeComponent();
        DataContext = viewModel;
        UpdateTitle();
        resource.PropertyChanged += Resource_PropertyChanged;
        Closed += (_, _) => resource.PropertyChanged -= Resource_PropertyChanged;
    }

    public ResourceItem Resource { get; }

    private void Resource_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResourceItem.Name))
        {
            UpdateTitle();
        }
    }

    private void UpdateTitle()
    {
        Title = string.IsNullOrWhiteSpace(Resource.Name)
            ? "Resource"
            : $"{Resource.Name} - Resource";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
