using System.Windows;
using TwoDoThree.Models;

namespace TwoDoThree.Views;

public partial class ImagePreviewWindow : Window
{
    public ImagePreviewWindow(ResourceItem resource)
    {
        InitializeComponent();
        DataContext = resource;
        Title = string.IsNullOrWhiteSpace(resource.Name)
            ? "Image"
            : $"{resource.Name} - Image";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
