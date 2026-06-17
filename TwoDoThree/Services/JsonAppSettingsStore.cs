using System.IO;
using System.Text.Json;
using TwoDoThree.Models;
using TaskItemStatus = TwoDoThree.Models.TaskStatus;

namespace TwoDoThree.Services;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Load()
    {
        var settings = new AppSettings();
        if (!File.Exists(AppStoragePaths.SettingsFilePath))
        {
            return settings;
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<AppSettingsSnapshot>(
                File.ReadAllText(AppStoragePaths.SettingsFilePath),
                JsonOptions);
            if (snapshot is null)
            {
                return settings;
            }

            ApplyEmail(settings.Email, snapshot.Email);
            settings.Tags.ReplaceTags(snapshot.Tags);
            settings.Database.ConnectionString = snapshot.Database.ConnectionString;
            settings.Surf2.IsEnabled = snapshot.Surf2.IsEnabled;
            settings.Surf2.ConnectionString = snapshot.Surf2.ConnectionString;
            settings.Surf2.ExecutablePath = snapshot.Surf2.ExecutablePath;
            ApplyTaskList(settings.TaskList, snapshot.TaskList);
        }
        catch (JsonException)
        {
            return settings;
        }
        catch (IOException)
        {
            return settings;
        }

        return settings;
    }

    public void Save(AppSettings settings)
    {
        AppStoragePaths.EnsureRootDirectory();
        var snapshot = new AppSettingsSnapshot
        {
            Email = EmailSettingsSnapshot.From(settings.Email),
            Tags = settings.Tags.Tags.ToList(),
            Database = DatabaseSettingsSnapshot.From(settings.Database),
            Surf2 = Surf2IntegrationSettingsSnapshot.From(settings.Surf2),
            TaskList = TaskListSettingsSnapshot.From(settings.TaskList)
        };

        File.WriteAllText(
            AppStoragePaths.SettingsFilePath,
            JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    private static void ApplyEmail(EmailSettings settings, EmailSettingsSnapshot snapshot)
    {
        settings.Source = snapshot.Source;
        settings.AccountAddress = snapshot.AccountAddress;
        settings.DisplayName = snapshot.DisplayName;
        settings.TenantId = snapshot.TenantId;
        settings.ClientId = snapshot.ClientId;
        settings.SyncIntervalMinutes = snapshot.SyncIntervalMinutes;
        settings.UseWindowsAuthentication = snapshot.UseWindowsAuthentication;
        settings.MaxInboxMessages = snapshot.MaxInboxMessages;
    }

    private static void ApplyTaskList(TaskListSettings settings, TaskListSettingsSnapshot snapshot)
    {
        settings.ReplaceVisibleColumns(snapshot.VisibleColumns.Count == 0
            ? TaskListSettings.DefaultVisibleColumns
            : snapshot.VisibleColumns);
        settings.ReplaceFilterSets(snapshot.FilterSets.Select(filterSet => filterSet.ToModel()));
        settings.SelectedFilterSetId = snapshot.SelectedFilterSetId;
    }

    private sealed class AppSettingsSnapshot
    {
        public EmailSettingsSnapshot Email { get; set; } = new();

        public List<string> Tags { get; set; } = [];

        public DatabaseSettingsSnapshot Database { get; set; } = new();

        public Surf2IntegrationSettingsSnapshot Surf2 { get; set; } = new();

        public TaskListSettingsSnapshot TaskList { get; set; } = new();
    }

    private sealed class DatabaseSettingsSnapshot
    {
        public string ConnectionString { get; set; } = string.Empty;

        public static DatabaseSettingsSnapshot From(DatabaseSettings settings)
        {
            return new DatabaseSettingsSnapshot
            {
                ConnectionString = settings.ConnectionString
            };
        }
    }

    private sealed class Surf2IntegrationSettingsSnapshot
    {
        public bool IsEnabled { get; set; }

        public string ConnectionString { get; set; } = string.Empty;

        public string ExecutablePath { get; set; } = string.Empty;

        public static Surf2IntegrationSettingsSnapshot From(Surf2IntegrationSettings settings)
        {
            return new Surf2IntegrationSettingsSnapshot
            {
                IsEnabled = settings.IsEnabled,
                ConnectionString = settings.ConnectionString,
                ExecutablePath = settings.ExecutablePath
            };
        }
    }

    private sealed class TaskListSettingsSnapshot
    {
        public List<TaskListColumn> VisibleColumns { get; set; } = [];

        public string SelectedFilterSetId { get; set; } = string.Empty;

        public List<TaskFilterSetSnapshot> FilterSets { get; set; } = [];

        public static TaskListSettingsSnapshot From(TaskListSettings settings)
        {
            return new TaskListSettingsSnapshot
            {
                VisibleColumns = settings.VisibleColumns.ToList(),
                SelectedFilterSetId = settings.SelectedFilterSetId,
                FilterSets = settings.FilterSets
                    .Select(TaskFilterSetSnapshot.From)
                    .ToList()
            };
        }
    }

    private sealed class TaskFilterSetSnapshot
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string IdContains { get; set; } = string.Empty;

        public string TitleContains { get; set; } = string.Empty;

        public string TagsContains { get; set; } = string.Empty;

        public string PocsContains { get; set; } = string.Empty;

        public string SurfScopeContains { get; set; } = string.Empty;

        public List<TaskItemStatus> IncludedStatuses { get; set; } = [];

        public DateTime? DueByFrom { get; set; }

        public DateTime? DueByTo { get; set; }

        public DateTime? CreatedOnFrom { get; set; }

        public DateTime? CreatedOnTo { get; set; }

        public DateTime? UpdatedOnFrom { get; set; }

        public DateTime? UpdatedOnTo { get; set; }

        public double? MinTimeSpentHours { get; set; }

        public double? MaxTimeSpentHours { get; set; }

        public static TaskFilterSetSnapshot From(TaskFilterSet filterSet)
        {
            return new TaskFilterSetSnapshot
            {
                Id = filterSet.Id,
                Name = filterSet.Name,
                IdContains = filterSet.IdContains,
                TitleContains = filterSet.TitleContains,
                TagsContains = filterSet.TagsContains,
                PocsContains = filterSet.PocsContains,
                SurfScopeContains = filterSet.SurfScopeContains,
                IncludedStatuses = filterSet.IncludedStatuses.ToList(),
                DueByFrom = filterSet.DueByFrom,
                DueByTo = filterSet.DueByTo,
                CreatedOnFrom = filterSet.CreatedOnFrom,
                CreatedOnTo = filterSet.CreatedOnTo,
                UpdatedOnFrom = filterSet.UpdatedOnFrom,
                UpdatedOnTo = filterSet.UpdatedOnTo,
                MinTimeSpentHours = filterSet.MinTimeSpentHours,
                MaxTimeSpentHours = filterSet.MaxTimeSpentHours
            };
        }

        public TaskFilterSet ToModel()
        {
            return new TaskFilterSet
            {
                Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id,
                Name = Name,
                IdContains = IdContains,
                TitleContains = TitleContains,
                TagsContains = TagsContains,
                PocsContains = PocsContains,
                SurfScopeContains = SurfScopeContains,
                IncludedStatuses = IncludedStatuses.ToList(),
                DueByFrom = DueByFrom,
                DueByTo = DueByTo,
                CreatedOnFrom = CreatedOnFrom,
                CreatedOnTo = CreatedOnTo,
                UpdatedOnFrom = UpdatedOnFrom,
                UpdatedOnTo = UpdatedOnTo,
                MinTimeSpentHours = MinTimeSpentHours,
                MaxTimeSpentHours = MaxTimeSpentHours
            };
        }
    }
}
