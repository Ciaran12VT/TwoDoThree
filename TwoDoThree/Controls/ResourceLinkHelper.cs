using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TwoDoThree.Models;
using TwoDoThree.ViewModels;

namespace TwoDoThree.Controls;

public static class ResourceLinkHelper
{
    public const string ResourceDataFormat = "TwoDoThree.ResourceItem";

    private static readonly Regex LinkRegex = new(@"\[\[(?<name>[^\]]+)\]\]", RegexOptions.Compiled);

    public static DataObject CreateDataObject(ResourceItem resource)
    {
        var data = new DataObject();
        data.SetData(ResourceDataFormat, resource);
        data.SetText(CreateToken(resource));
        return data;
    }

    public static bool TryGetDraggedResource(DragEventArgs e, out ResourceItem? resource)
    {
        resource = e.Data.GetData(ResourceDataFormat) as ResourceItem;
        return resource is not null;
    }

    public static string CreateToken(ResourceItem resource)
    {
        return $"[[{resource.Name}]]";
    }

    public static string? GetResourceNameAtOffset(string text, int offset)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        offset = Math.Clamp(offset, 0, text.Length);
        foreach (Match match in LinkRegex.Matches(text))
        {
            if (offset >= match.Index && offset <= match.Index + match.Length)
            {
                return match.Groups["name"].Value;
            }
        }

        return null;
    }

    public static bool TryOpenResource(string? resourceName, DependencyObject source)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
        {
            return false;
        }

        var viewModel = FindTaskDetailViewModel(source);
        var resource = viewModel?.Task.Resources.FirstOrDefault(resource =>
            string.Equals(resource.Name, resourceName.Trim(), StringComparison.OrdinalIgnoreCase));

        if (resource is null || viewModel is null)
        {
            return false;
        }

        viewModel.SelectedResource = resource;
        return true;
    }

    public static TaskDetailViewModel? FindTaskDetailViewModel(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: TaskDetailViewModel viewModel })
            {
                return viewModel;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
