using System.Text.Json;

namespace TwoDoThree.Models;

// Adapted from Surf's folder-owned membership: existing Kind/Content columns store
// only resource IDs, never filesystem operations or replacement file paths.
public sealed class VirtualFolderData
{
    public List<Guid> ChildResourceIds { get; set; } = [];

    public static VirtualFolderData Read(ResourceItem folder)
    {
        try
        {
            var data = JsonSerializer.Deserialize<VirtualFolderData>(folder.Content) ?? new();
            data.ChildResourceIds ??= [];
            return data;
        }
        catch (JsonException) { return new(); }
    }

    public void Save(ResourceItem folder) => folder.Content = JsonSerializer.Serialize(this);
}
