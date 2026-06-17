using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TwoDoThree.Models;
using TwoDoThree.Services;
using TwoDoThree.Views;

namespace TwoDoThree.ViewModels;

public sealed class TaskDetailViewModel : ObservableObject
{
    private ResourceItem? selectedResource;
    private ActionItem? selectedAction;
    private string resourceSearchText = string.Empty;
    private readonly Surf2IntegrationSettings surf2Settings;
    private readonly ISurf2IntegrationService surf2IntegrationService;
    private readonly ISurf2Launcher surf2Launcher;
    private Surf2ScopeOption? selectedSurfScope;
    private IReadOnlyList<Surf2ResourceCandidate> surfResources = [];
    private string loadedSurfResourceScopeId = string.Empty;
    private string surf2Status = string.Empty;
    private bool isInitializingSurfScope;
    private bool isLoadingSurfResources;

    public TaskDetailViewModel(
        TaskItem task,
        TagSettings tagSettings,
        Surf2IntegrationSettings surf2Settings,
        ISurf2IntegrationService surf2IntegrationService,
        ISurf2Launcher surf2Launcher)
    {
        Task = task;
        TagSettings = tagSettings;
        this.surf2Settings = surf2Settings;
        this.surf2IntegrationService = surf2IntegrationService;
        this.surf2Launcher = surf2Launcher;
        AddTextResourceCommand = new RelayCommand(_ => AddTextResource());
        AddSheetResourceCommand = new RelayCommand(_ => AddSheetResource());
        AddCodeResourceCommand = new RelayCommand(_ => AddCodeResource());
        AddImageResourceCommand = new RelayCommand(_ => AddImageResource());
        AddAudioResourceCommand = new RelayCommand(_ => AddFileResource(ResourceKind.Audio));
        AddSurfResourceCommand = new RelayCommand(async _ => await AddSurfResourceAsync(), _ => CanAddSurfResource);
        SaveSelectedResourceCommand = new RelayCommand(_ => SaveSelectedResource(), _ => SelectedResource is not null);
        AddActionCommand = new RelayCommand(_ => AddAction());
        RefreshSurfScopesCommand = new RelayCommand(async _ => await InitializeSurf2Async());

        RefreshResourceGroups();
        RenumberActions(Task.Actions);
    }

    public TaskItem Task { get; }

    public TagSettings TagSettings { get; }

    public ObservableCollection<string> AvailableTags => TagSettings.Tags;

    public IReadOnlyList<TwoDoThree.Models.TaskStatus> TaskStatusValues { get; } = Enum.GetValues<TwoDoThree.Models.TaskStatus>();

    public ObservableCollection<ActionItem> Actions => Task.Actions;

    public ObservableCollection<ResourceGroup> ResourceGroups { get; } = new();

    public ObservableCollection<Surf2ScopeOption> SurfScopes { get; } = new();

    public ActionItem? SelectedAction
    {
        get => selectedAction;
        set => SetProperty(ref selectedAction, value);
    }

    public ResourceItem? SelectedResource
    {
        get => selectedResource;
        set
        {
            if (SetProperty(ref selectedResource, value)
                && SaveSelectedResourceCommand is RelayCommand command)
            {
                command.RaiseCanExecuteChanged();
            }
        }
    }

    public string ResourceSearchText
    {
        get => resourceSearchText;
        set
        {
            if (SetProperty(ref resourceSearchText, value))
            {
                RefreshResourceGroups();
            }
        }
    }

    public bool IsSurf2IntegrationAvailable => surf2Settings.IsConfigured;

    public bool CanAddSurfResource => IsSurf2IntegrationAvailable && SelectedSurfScope is not null && !isLoadingSurfResources;

    public Surf2ScopeOption? SelectedSurfScope
    {
        get => selectedSurfScope;
        set
        {
            if (!SetProperty(ref selectedSurfScope, value))
            {
                return;
            }

            loadedSurfResourceScopeId = string.Empty;
            surfResources = [];
            RaiseSurfResourceStateChanged();

            if (!isInitializingSurfScope)
            {
                Task.SurfScopeId = value?.ScopeId ?? string.Empty;
                Task.SurfScopeName = value?.Name ?? string.Empty;
                TouchTask();
                if (value is not null)
                {
                    _ = LoadSurfResourcesForSelectedScopeAsync();
                }
            }
        }
    }

    public string Surf2Status
    {
        get => surf2Status;
        set => SetProperty(ref surf2Status, value);
    }

    public ICommand AddTextResourceCommand { get; }

    public ICommand AddSheetResourceCommand { get; }

    public ICommand AddCodeResourceCommand { get; }

    public ICommand AddImageResourceCommand { get; }

    public ICommand AddAudioResourceCommand { get; }

    public ICommand AddSurfResourceCommand { get; }

    public ICommand SaveSelectedResourceCommand { get; }

    public ICommand AddActionCommand { get; }

    public ICommand RefreshSurfScopesCommand { get; }

    public void RefreshResourceGroups(ResourceItem? preferredResource = null)
    {
        var resourceToSelect = preferredResource ?? SelectedResource;

        RebuildResourceGroups();

        SelectedResource = resourceToSelect is not null && Task.Resources.Contains(resourceToSelect)
            ? resourceToSelect
            : Task.Resources.FirstOrDefault();
    }

    public async Task InitializeSurf2Async()
    {
        isInitializingSurfScope = true;
        try
        {
            SurfScopes.Clear();
            SelectedSurfScope = null;
        }
        finally
        {
            isInitializingSurfScope = false;
        }

        surfResources = [];
        loadedSurfResourceScopeId = string.Empty;

        if (!IsSurf2IntegrationAvailable)
        {
            Surf2Status = "Surf2 integration is disabled.";
            RaiseSurfResourceStateChanged();
            return;
        }

        try
        {
            Surf2Status = "Loading Surf2 scopes...";
            IReadOnlyList<Surf2ScopeOption> scopes = await surf2IntegrationService.LoadScopesAsync(
                surf2Settings,
                CancellationToken.None);
            foreach (Surf2ScopeOption scope in scopes)
            {
                SurfScopes.Add(scope);
            }

            isInitializingSurfScope = true;
            try
            {
                SelectedSurfScope = SurfScopes.FirstOrDefault(scope =>
                    string.Equals(scope.ScopeId, Task.SurfScopeId, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                isInitializingSurfScope = false;
            }

            Surf2Status = SurfScopes.Count == 0
                ? "No Surf2 scopes were found."
                : $"{SurfScopes.Count} Surf2 scope{(SurfScopes.Count == 1 ? string.Empty : "s")} available.";

            if (SelectedSurfScope is not null)
            {
                await LoadSurfResourcesForSelectedScopeAsync();
            }
        }
        catch (Exception ex)
        {
            Surf2Status = $"Could not load Surf2 scopes: {ex.Message}";
        }
        finally
        {
            RaiseSurfResourceStateChanged();
        }
    }

    public async Task<IReadOnlyList<Surf2ResourceCandidate>> GetSurfResourcesForInsertionAsync()
    {
        await LoadSurfResourcesForSelectedScopeAsync();
        return surfResources;
    }

    public ResourceItem GetOrCreateSurfResource(Surf2ResourceCandidate candidate)
    {
        ResourceItem? existing = Task.Resources.FirstOrDefault(resource =>
            resource.Kind == ResourceKind.SurfResource &&
            SurfResourceLink.TryParse(resource.Content, out SurfResourceLink link) &&
            link.Matches(candidate));
        if (existing is not null)
        {
            return existing;
        }

        var resource = new ResourceItem
        {
            Name = CreateUniqueResourceName(candidate.Name),
            Kind = ResourceKind.SurfResource,
            Content = SurfResourceLink.FromCandidate(candidate).ToJson()
        };
        AddResource(resource);
        return resource;
    }

    public void OpenLinkedResource(ResourceItem resource)
    {
        if (resource.Kind == ResourceKind.SurfResource)
        {
            _ = OpenSurfResourceAsync(resource);
            return;
        }

        SelectedResource = resource;
    }

    private async Task LoadSurfResourcesForSelectedScopeAsync()
    {
        if (!IsSurf2IntegrationAvailable || SelectedSurfScope is null)
        {
            surfResources = [];
            loadedSurfResourceScopeId = string.Empty;
            RaiseSurfResourceStateChanged();
            return;
        }

        if (string.Equals(loadedSurfResourceScopeId, SelectedSurfScope.ScopeId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        isLoadingSurfResources = true;
        RaiseSurfResourceStateChanged();

        try
        {
            Surf2Status = $"Loading Surf2 resources for {SelectedSurfScope.DisplayName}...";
            surfResources = await surf2IntegrationService.LoadResourcesAsync(
                surf2Settings,
                SelectedSurfScope.ScopeId,
                CancellationToken.None);
            loadedSurfResourceScopeId = SelectedSurfScope.ScopeId;
            Surf2Status = $"{surfResources.Count} Surf2 resource{(surfResources.Count == 1 ? string.Empty : "s")} available for {SelectedSurfScope.DisplayName}.";
        }
        catch (Exception ex)
        {
            surfResources = [];
            loadedSurfResourceScopeId = string.Empty;
            Surf2Status = $"Could not load Surf2 resources: {ex.Message}";
        }
        finally
        {
            isLoadingSurfResources = false;
            RaiseSurfResourceStateChanged();
        }
    }

    private async Task AddSurfResourceAsync()
    {
        IReadOnlyList<Surf2ResourceCandidate> resources = await GetSurfResourcesForInsertionAsync();
        if (SelectedSurfScope is null)
        {
            Surf2Status = "Choose a Surf2 scope first.";
            return;
        }

        if (resources.Count == 0)
        {
            Surf2Status = $"No Surf2 resources are available for {SelectedSurfScope.DisplayName}.";
            return;
        }

        var picker = new SurfResourcePickerWindow(resources)
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
        };
        if (picker.ShowDialog() == true && picker.SelectedResource is not null)
        {
            ResourceItem resource = GetOrCreateSurfResource(picker.SelectedResource);
            SelectedResource = resource;
            Surf2Status = $"Added Surf2 resource '{resource.Name}'.";
        }
    }

    private async Task OpenSurfResourceAsync(ResourceItem resource)
    {
        if (!SurfResourceLink.TryParse(resource.Content, out SurfResourceLink link))
        {
            Surf2Status = "This Surf resource is missing its target details.";
            return;
        }

        try
        {
            Surf2Status = $"Opening '{resource.Name}' in Surf2...";
            await surf2Launcher.OpenResourceAsync(surf2Settings, link, CancellationToken.None);
            Surf2Status = $"Requested Surf2 resource '{resource.Name}'.";
        }
        catch (Exception ex)
        {
            Surf2Status = $"Could not open Surf2 resource: {ex.Message}";
            MessageBox.Show(
                Surf2Status,
                "Surf2",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    public void SetTaskStatus(TwoDoThree.Models.TaskStatus status, string statusMessage = "")
    {
        Task.SetStatus(status, statusMessage);
    }

    public static void RenumberActions(IEnumerable<ActionItem> actions)
    {
        var counters = new List<int>();
        var previousIndent = 0;
        var isFirst = true;

        foreach (var action in actions)
        {
            var indent = action.IndentLevel;
            if (isFirst)
            {
                indent = 0;
            }
            else
            {
                indent = Math.Min(indent, previousIndent + 1);
            }

            action.IndentLevel = indent;

            while (counters.Count <= indent)
            {
                counters.Add(0);
            }

            counters[indent]++;

            if (counters.Count > indent + 1)
            {
                counters.RemoveRange(indent + 1, counters.Count - indent - 1);
            }

            action.ActionNumber = string.Join(".", counters.Take(indent + 1));
            previousIndent = indent;
            isFirst = false;
        }
    }

    public void IndentAction(ActionItem action)
    {
        var index = Actions.IndexOf(action);
        if (index <= 0)
        {
            return;
        }

        var maxIndent = Actions[index - 1].IndentLevel + 1;
        if (action.IndentLevel < maxIndent)
        {
            action.IndentLevel++;
            TouchTask();
            RenumberActions(Actions);
        }
    }

    public void UnindentAction(ActionItem action)
    {
        if (action.IndentLevel == 0)
        {
            return;
        }

        action.IndentLevel--;
        TouchTask();
        RenumberActions(Actions);
    }

    public void MoveActionUp(ActionItem action)
    {
        var index = Actions.IndexOf(action);
        if (index <= 0)
        {
            return;
        }

        Actions.Move(index, index - 1);
        TouchTask();
        RenumberActions(Actions);
    }

    public void MoveActionDown(ActionItem action)
    {
        var index = Actions.IndexOf(action);
        if (index < 0 || index >= Actions.Count - 1)
        {
            return;
        }

        Actions.Move(index, index + 1);
        TouchTask();
        RenumberActions(Actions);
    }

    public void SetActionStatus(ActionItem action, ActionStatus status)
    {
        action.Status = status;
        TouchTask();
    }

    public void DeleteAction(ActionItem action)
    {
        var index = Actions.IndexOf(action);
        if (index < 0)
        {
            return;
        }

        Actions.RemoveAt(index);
        TouchTask();
        RenumberActions(Actions);
        SelectedAction = Actions.Count == 0
            ? null
            : Actions[Math.Min(index, Actions.Count - 1)];
    }

    public void AddActionBelow(ActionItem action)
    {
        var index = Actions.IndexOf(action);
        if (index < 0)
        {
            return;
        }

        var newAction = CreateAction(action.IndentLevel);
        Actions.Insert(index + 1, newAction);
        SelectedAction = newAction;
        TouchTask();
        RenumberActions(Actions);
    }

    public void CycleActionStatus(ActionItem action, int direction)
    {
        var statuses = Enum.GetValues<ActionStatus>();
        var index = Array.IndexOf(statuses, action.Status);
        if (index < 0)
        {
            index = 0;
        }

        var nextIndex = (index + direction + statuses.Length) % statuses.Length;
        SetActionStatus(action, statuses[nextIndex]);
    }

    private void AddAction()
    {
        if (SelectedAction is { } selected)
        {
            AddChildAction(selected);
            return;
        }

        var action = CreateAction(0);
        Actions.Add(action);
        SelectedAction = action;
        TouchTask();
        RenumberActions(Actions);
    }

    private void AddChildAction(ActionItem parent)
    {
        var index = Actions.IndexOf(parent);
        if (index < 0)
        {
            return;
        }

        var action = CreateAction(parent.IndentLevel + 1);
        Actions.Insert(index + 1, action);
        SelectedAction = action;
        TouchTask();
        RenumberActions(Actions);
    }

    private static ActionItem CreateAction(int indentLevel)
    {
        return new ActionItem
        {
            ActionText = string.Empty,
            IndentLevel = indentLevel,
            Status = ActionStatus.NotStarted
        };
    }

    private void AddTextResource()
    {
        AddResource(new ResourceItem
        {
            Name = $"Text note {Task.Resources.Count(r => r.Kind == ResourceKind.Text) + 1}",
            Kind = ResourceKind.Text,
            Content = "Write notes for this task here."
        });
    }

    private void SaveSelectedResource()
    {
        if (SelectedResource is null)
        {
            return;
        }

        var resource = SelectedResource;
        var dialog = new SaveFileDialog
        {
            Title = "Save resource",
            FileName = GetDefaultFileName(resource),
            Filter = GetSaveFilter(resource),
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (resource.Kind is ResourceKind.Image or ResourceKind.Audio
            && File.Exists(resource.Content))
        {
            File.Copy(resource.Content, dialog.FileName, overwrite: true);
        }
        else if (resource.Kind == ResourceKind.Sheet)
        {
            File.WriteAllText(dialog.FileName, SheetResourceSerializer.ToCsv(resource.Content));
        }
        else
        {
            File.WriteAllText(dialog.FileName, resource.Content);
        }
    }

    private static string GetDefaultFileName(ResourceItem resource)
    {
        var name = string.IsNullOrWhiteSpace(resource.Name) ? "Resource" : resource.Name;
        var extension = resource.Kind switch
        {
            ResourceKind.Sheet => ".csv",
            ResourceKind.CodeSnippet => GetCodeExtension(resource.CodeLanguage),
            ResourceKind.Image => Path.GetExtension(resource.Content),
            ResourceKind.Audio => Path.GetExtension(resource.Content),
            _ => ".txt"
        };

        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".txt";
        }

        return Path.ChangeExtension(name, extension);
    }

    private static string GetSaveFilter(ResourceItem resource)
    {
        return resource.Kind switch
        {
            ResourceKind.Sheet => "CSV files|*.csv|All files|*.*",
            ResourceKind.CodeSnippet => "Code files|*.cs;*.xml;*.html;*.css;*.js;*.ps1;*.cpp;*.java;*.php;*.vb;*.txt|All files|*.*",
            ResourceKind.Image => "Image files|*.png;*.jpg;*.jpeg;*.gif;*.bmp|All files|*.*",
            ResourceKind.Audio => "Audio files|*.mp3;*.wav;*.wma;*.m4a|All files|*.*",
            _ => "Text files|*.txt|All files|*.*"
        };
    }

    private static string GetCodeExtension(string language)
    {
        return language switch
        {
            "C#" => ".cs",
            "XML" => ".xml",
            "HTML" => ".html",
            "CSS" => ".css",
            "JavaScript" => ".js",
            "PowerShell" => ".ps1",
            "C++" => ".cpp",
            "Java" => ".java",
            "PHP" => ".php",
            "VBNET" => ".vb",
            _ => ".txt"
        };
    }

    private void AddSheetResource()
    {
        AddResource(new ResourceItem
        {
            Name = $"Sheet {Task.Resources.Count(r => r.Kind == ResourceKind.Sheet) + 1}",
            Kind = ResourceKind.Sheet,
            Content = SheetResourceSerializer.CreateDefaultContent()
        });
    }

    private void AddCodeResource()
    {
        AddResource(new ResourceItem
        {
            Name = $"Code snippet {Task.Resources.Count(r => r.Kind == ResourceKind.CodeSnippet) + 1}",
            Kind = ResourceKind.CodeSnippet,
            Content = "// Paste or type a useful code snippet here.",
            CodeLanguage = "C#"
        });
    }

    private void AddImageResource()
    {
        if (TryAddClipboardImageResource())
        {
            return;
        }

        AddFileResource(ResourceKind.Image);
    }

    private bool TryAddClipboardImageResource()
    {
        try
        {
            if (!Clipboard.ContainsImage())
            {
                return false;
            }

            var image = Clipboard.GetImage();
            if (image is null)
            {
                return false;
            }

            var resourceName = $"Clipboard image {Task.Resources.Count(r => r.Kind == ResourceKind.Image) + 1}";
            var imagePath = SaveClipboardImage(image, resourceName);
            AddResource(new ResourceItem
            {
                Name = resourceName,
                Kind = ResourceKind.Image,
                Content = imagePath
            });

            return true;
        }
        catch
        {
            return false;
        }
    }

    private string SaveClipboardImage(BitmapSource image, string resourceName)
    {
        var resourcesDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "2do3",
            "Resources",
            $"Task-{Task.Id}");
        Directory.CreateDirectory(resourcesDirectory);

        var imagePath = Path.Combine(resourcesDirectory, $"{resourceName}-{DateTime.Now:yyyyMMdd-HHmmssfff}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        using var stream = File.Create(imagePath);
        encoder.Save(stream);
        return imagePath;
    }

    private void AddFileResource(ResourceKind kind)
    {
        var dialog = new OpenFileDialog
        {
            Title = kind == ResourceKind.Image ? "Select image resource" : "Select audio resource",
            Filter = kind == ResourceKind.Image
                ? "Image files|*.png;*.jpg;*.jpeg;*.gif;*.bmp|All files|*.*"
                : "Audio files|*.mp3;*.wav;*.wma;*.m4a|All files|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        AddResource(new ResourceItem
        {
            Name = Path.GetFileName(dialog.FileName),
            Kind = kind,
            Content = dialog.FileName
        });
    }

    private void AddResource(ResourceItem resource)
    {
        Task.Resources.Add(resource);
        RefreshResourceGroups(resource);
        TouchTask();
    }

    private string CreateUniqueResourceName(string preferredName)
    {
        string baseName = string.IsNullOrWhiteSpace(preferredName)
            ? $"Surf resource {Task.Resources.Count(r => r.Kind == ResourceKind.SurfResource) + 1}"
            : preferredName.Trim();
        if (!Task.Resources.Any(resource => string.Equals(resource.Name, baseName, StringComparison.OrdinalIgnoreCase)))
        {
            return baseName;
        }

        for (int suffix = 2; suffix < int.MaxValue; suffix++)
        {
            string candidate = $"{baseName} ({suffix})";
            if (!Task.Resources.Any(resource => string.Equals(resource.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }

        return $"{baseName} {Guid.NewGuid():N}";
    }

    private void RaiseSurfResourceStateChanged()
    {
        OnPropertyChanged(nameof(IsSurf2IntegrationAvailable));
        OnPropertyChanged(nameof(CanAddSurfResource));
        if (AddSurfResourceCommand is RelayCommand addSurfResourceCommand)
        {
            addSurfResourceCommand.RaiseCanExecuteChanged();
        }
    }

    private void RebuildResourceGroups()
    {
        ResourceGroups.Clear();
        var filter = ResourceSearchText.Trim();

        foreach (var kind in Enum.GetValues<ResourceKind>())
        {
            var group = new ResourceGroup(kind);
            foreach (var resource in Task.Resources.Where(resource =>
                         resource.Kind == kind
                         && (string.IsNullOrWhiteSpace(filter)
                             || resource.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))))
            {
                group.Resources.Add(resource);
            }

            if (string.IsNullOrWhiteSpace(filter) || group.Resources.Count > 0)
            {
                ResourceGroups.Add(group);
            }
        }
    }

    private void TouchTask()
    {
        Task.UpdatedOn = DateTime.Now;
    }
}
