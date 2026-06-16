using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TwoDoThree.Controls;

public static class ResourceLinkTextBox
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ResourceLinkTextBox),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject dependencyObject)
    {
        return (bool)dependencyObject.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(DependencyObject dependencyObject, bool value)
    {
        dependencyObject.SetValue(IsEnabledProperty, value);
    }

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not TextBox textBox)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            textBox.AllowDrop = true;
            textBox.PreviewDragOver += TextBox_PreviewDragOver;
            textBox.Drop += TextBox_Drop;
            textBox.MouseDoubleClick += TextBox_MouseDoubleClick;
        }
        else
        {
            textBox.PreviewDragOver -= TextBox_PreviewDragOver;
            textBox.Drop -= TextBox_Drop;
            textBox.MouseDoubleClick -= TextBox_MouseDoubleClick;
        }
    }

    private static void TextBox_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = ResourceLinkHelper.TryGetDraggedResource(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private static void TextBox_Drop(object sender, DragEventArgs e)
    {
        if (sender is not TextBox textBox || !ResourceLinkHelper.TryGetDraggedResource(e, out var resource) || resource is null)
        {
            return;
        }

        var index = textBox.GetCharacterIndexFromPoint(e.GetPosition(textBox), snapToText: true);
        textBox.CaretIndex = index < 0 ? textBox.Text.Length : index;
        InsertText(textBox, ResourceLinkHelper.CreateToken(resource));
        e.Handled = true;
    }

    private static void TextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        var resourceName = ResourceLinkHelper.GetResourceNameAtOffset(textBox.Text, textBox.CaretIndex);
        if (ResourceLinkHelper.TryOpenResource(resourceName, textBox))
        {
            e.Handled = true;
        }
    }

    private static void InsertText(TextBox textBox, string text)
    {
        var selectionStart = textBox.SelectionStart;
        textBox.Text = textBox.Text.Remove(selectionStart, textBox.SelectionLength).Insert(selectionStart, text);
        textBox.CaretIndex = selectionStart + text.Length;
    }
}
