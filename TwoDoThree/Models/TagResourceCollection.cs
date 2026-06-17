using System.Collections.ObjectModel;
using TwoDoThree.ViewModels;

namespace TwoDoThree.Models;

public sealed class TagResourceCollection : ObservableObject
{
    private string tag = string.Empty;

    public string Tag
    {
        get => tag;
        set => SetProperty(ref tag, value);
    }

    public ObservableCollection<ResourceItem> Resources { get; } = new();
}
