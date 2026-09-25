using System.Collections.ObjectModel;
using System.Collections;
using System.IO;
using System.Text.Json.Serialization;
using TwoDoThree.ViewModels;

namespace TwoDoThree.Models;

public sealed class ResourceItem : ObservableObject
{
    private Guid id = Guid.NewGuid();
    private string name = string.Empty;
    private ResourceKind kind;
    private string content = string.Empty;
    private string formattedContent = string.Empty;
    private string codeLanguage = "C#";
    private string emailMessageId = string.Empty;
    private string emailFrom = string.Empty;
    private string emailSubject = string.Empty;
    private DateTime? emailReceivedOn;
    private Guid? parentVirtualFolderId;
    private bool isExpanded;

    // Derived from virtual folders' stored membership, never a separate persisted relationship.
    [JsonIgnore]
    public Guid? ParentVirtualFolderId
    {
        get => parentVirtualFolderId;
        set => SetProperty(ref parentVirtualFolderId, value);
    }

    [JsonIgnore]
    public bool IsExpanded
    {
        get => isExpanded;
        set => SetProperty(ref isExpanded, value);
    }

    [JsonIgnore]
    public ObservableCollection<ResourceItem> VirtualChildren { get; } = new();

    [JsonIgnore]
    public IEnumerable TreeChildren => Kind == ResourceKind.VirtualFolder ? VirtualChildren : FolderChildren;

    [JsonIgnore]
    public bool IsFileTreeResource => Kind is ResourceKind.File or ResourceKind.Folder or ResourceKind.VirtualFolder;

    [JsonIgnore]
    public bool IsFolderIcon => Kind is ResourceKind.Folder or ResourceKind.VirtualFolder;

    // Folder geometry and colors adapted from Surf2.Models.FileSystemNode.
    [JsonIgnore]
    public string FolderIconGeometry => "M2.5,5.5 L7,5.5 L8.5,7.2 L15.5,7.2 Q16.5,7.2 16.5,8.2 L16.5,14.5 Q16.5,15.5 15.5,15.5 L2.5,15.5 Q1.5,15.5 1.5,14.5 L1.5,6.5 Q1.5,5.5 2.5,5.5 Z";

    public Guid Id
    {
        get => id;
        set => SetProperty(ref id, value == Guid.Empty ? Guid.NewGuid() : value);
    }

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public ResourceKind Kind
    {
        get => kind;
        set => SetProperty(ref kind, value);
    }

    public string Content
    {
        get => content;
        set
        {
            if (SetProperty(ref content, value))
            {
                OnPropertyChanged(nameof(SurfResourcePath));
                OnPropertyChanged(nameof(SurfResourceSummary));
            }
        }
    }

    public string FormattedContent
    {
        get => formattedContent;
        set => SetProperty(ref formattedContent, value);
    }

    public string CodeLanguage
    {
        get => codeLanguage;
        set => SetProperty(ref codeLanguage, value);
    }

    public string EmailMessageId
    {
        get => emailMessageId;
        set => SetProperty(ref emailMessageId, value);
    }

    public string EmailFrom
    {
        get => emailFrom;
        set => SetProperty(ref emailFrom, value);
    }

    public string EmailSubject
    {
        get => emailSubject;
        set => SetProperty(ref emailSubject, value);
    }

    public DateTime? EmailReceivedOn
    {
        get => emailReceivedOn;
        set => SetProperty(ref emailReceivedOn, value);
    }

    public string SurfResourcePath =>
        SurfResourceLink.TryParse(Content, out SurfResourceLink link)
            ? link.ResourcePath
            : Content;

    public string SurfResourceSummary =>
        SurfResourceLink.TryParse(Content, out SurfResourceLink link)
            ? link.DisplaySummary
            : Content;

    [JsonIgnore]
    public ObservableCollection<FileSystemResourceNode> FolderChildren { get; } = new();

    [JsonIgnore]
    public bool FolderChildrenLoaded { get; set; }

    [JsonIgnore]
    public string FileIcon => Kind switch
    {
        ResourceKind.Folder => "📁",
        ResourceKind.File when Path.GetExtension(Content).Equals(".pdf", StringComparison.OrdinalIgnoreCase) => "PDF",
        ResourceKind.File when Path.GetExtension(Content).Equals(".doc", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(Content).Equals(".docx", StringComparison.OrdinalIgnoreCase) => "W",
        ResourceKind.File => "📄",
        _ => string.Empty
    };

    [JsonIgnore]
    public string FileIconColor => Kind == ResourceKind.VirtualFolder ? "#C2410C"
        : Kind == ResourceKind.Folder ? "#B47900" : Path.GetExtension(Content).ToLowerInvariant() switch
    {
        ".pdf" => "#B91C1C",
        ".doc" or ".docx" => "#1D4ED8",
        _ => "#64748B"
    };
}
