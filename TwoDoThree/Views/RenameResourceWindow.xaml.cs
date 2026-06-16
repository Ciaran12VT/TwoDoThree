using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace TwoDoThree.Views;

public partial class RenameResourceWindow : Window, INotifyPropertyChanged
{
    private string resourceName;

    public RenameResourceWindow(string currentName)
    {
        InitializeComponent();
        resourceName = currentName;
        DataContext = this;
        Loaded += (_, _) =>
        {
            ResourceNameTextBox.Focus();
            ResourceNameTextBox.SelectAll();
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ResourceName
    {
        get => resourceName;
        set
        {
            if (resourceName == value)
            {
                return;
            }

            resourceName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResourceName)));
        }
    }

    private void RenameButton_Click(object sender, RoutedEventArgs e)
    {
        ResourceName = ResourceName.Trim();
        if (string.IsNullOrWhiteSpace(ResourceName))
        {
            ResourceNameTextBox.Focus();
            ResourceNameTextBox.SelectAll();
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ResourceNameTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ResourceNameTextBox.SelectAll();
    }
}
