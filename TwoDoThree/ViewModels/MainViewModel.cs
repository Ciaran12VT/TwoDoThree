using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Data;
using TwoDoThree.Models;
using TwoDoThree.Services;
using TaskItemStatus = TwoDoThree.Models.TaskStatus;

namespace TwoDoThree.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IEmailProvider emailProvider;
    private readonly IEmailImportService emailImportService;
    private readonly IEmailCacheStore emailCacheStore;
    private readonly ITaskStore taskStore;
    private readonly HashSet<TaskItem> pendingTaskSaves = new();
    private readonly DispatcherTimer activeTaskTimer;
    private readonly DispatcherTimer emailSyncTimer;
    private readonly DispatcherTimer taskPersistenceTimer;
    private bool isEnforcingActiveTask;
    private bool isEmailSyncing;
    private int nextTaskId = 1001;
    private string emailSyncStatus = string.Empty;
    private string taskPersistenceStatus = string.Empty;
    private string emailSearchText = string.Empty;
    private string taskSearchText = string.Empty;
    private EmailMessage? taskEmailFilter;
    private EmailMessage? selectedEmail;
    private TaskItem? selectedTask;
    private TaskListViewMode taskListViewMode = TaskListViewMode.Grid;
    private bool isEmailSectionExpanded = true;
    private bool isTaskSectionExpanded = true;

    public MainViewModel(
        AppSettings settings,
        IEmailProvider emailProvider,
        IEmailImportService emailImportService,
        IEmailCacheStore emailCacheStore,
        ITaskStore taskStore)
    {
        Settings = settings;
        this.emailProvider = emailProvider;
        this.emailImportService = emailImportService;
        this.emailCacheStore = emailCacheStore;
        this.taskStore = taskStore;
        EmailsView = CollectionViewSource.GetDefaultView(Emails);
        TasksView = CollectionViewSource.GetDefaultView(Tasks);
        EmailsView.Filter = FilterEmail;
        TasksView.Filter = FilterTask;

        taskPersistenceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        taskPersistenceTimer.Tick += (_, _) => SavePendingTasks();

        LoadCachedEmails();
        LoadTasksFromStore();
        RefreshEmailTaskAssociations();
        RecalculateAllTaskTimeSpent();

        activeTaskTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        activeTaskTimer.Tick += (_, _) => UpdateActiveTaskTimeSpent();
        activeTaskTimer.Start();

        emailSyncTimer = new DispatcherTimer();
        emailSyncTimer.Tick += async (_, _) => await RefreshEmailsAsync(null, allowInteractiveSignIn: false);
        UpdateEmailSyncInterval();
        emailSyncTimer.Start();
    }

    public AppSettings Settings { get; }

    public ObservableCollection<EmailMessage> Emails { get; } = new();

    public ObservableCollection<TaskItem> Tasks { get; } = new();

    public ObservableCollection<TaskBucketViewModel> TagTaskBuckets { get; } = new();

    public ObservableCollection<TaskBucketViewModel> StatusTaskBuckets { get; } = new();

    public ICollectionView EmailsView { get; }

    public ICollectionView TasksView { get; }

    public string EmailSearchText
    {
        get => emailSearchText;
        set
        {
            if (SetProperty(ref emailSearchText, value))
            {
                EmailsView.Refresh();
            }
        }
    }

    public string TaskSearchText
    {
        get => taskSearchText;
        set
        {
            if (SetProperty(ref taskSearchText, value))
            {
                TasksView.Refresh();
                RefreshTaskBuckets();
            }
        }
    }

    public EmailMessage? SelectedEmail
    {
        get => selectedEmail;
        set => SetProperty(ref selectedEmail, value);
    }

    public TaskItem? SelectedTask
    {
        get => selectedTask;
        set => SetProperty(ref selectedTask, value);
    }

    public TaskListViewMode TaskListViewMode
    {
        get => taskListViewMode;
        set
        {
            if (SetProperty(ref taskListViewMode, value))
            {
                OnPropertyChanged(nameof(IsGridTaskView));
                OnPropertyChanged(nameof(IsTagBucketTaskView));
                OnPropertyChanged(nameof(IsStatusKanbanTaskView));
                RefreshTaskBuckets();
            }
        }
    }

    public bool IsGridTaskView
    {
        get => TaskListViewMode == TaskListViewMode.Grid;
        set
        {
            if (value)
            {
                TaskListViewMode = TaskListViewMode.Grid;
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    public bool IsTagBucketTaskView
    {
        get => TaskListViewMode == TaskListViewMode.TagBuckets;
        set
        {
            if (value)
            {
                TaskListViewMode = TaskListViewMode.TagBuckets;
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    public bool IsStatusKanbanTaskView
    {
        get => TaskListViewMode == TaskListViewMode.StatusKanban;
        set
        {
            if (value)
            {
                TaskListViewMode = TaskListViewMode.StatusKanban;
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    public bool IsEmailSectionExpanded
    {
        get => isEmailSectionExpanded;
        set => SetProperty(ref isEmailSectionExpanded, value);
    }

    public bool IsTaskSectionExpanded
    {
        get => isTaskSectionExpanded;
        set => SetProperty(ref isTaskSectionExpanded, value);
    }

    public string TaskEmailFilterSummary => taskEmailFilter is null
        ? string.Empty
        : $"Email: {taskEmailFilter.Subject}";

    public bool HasTaskEmailFilter => taskEmailFilter is not null;

    public string EmailSyncStatus
    {
        get => emailSyncStatus;
        set => SetProperty(ref emailSyncStatus, value);
    }

    public string TaskPersistenceStatus
    {
        get => taskPersistenceStatus;
        set => SetProperty(ref taskPersistenceStatus, value);
    }

    public bool IsEmailSyncing
    {
        get => isEmailSyncing;
        set
        {
            if (SetProperty(ref isEmailSyncing, value))
            {
                OnPropertyChanged(nameof(CanRefreshEmail));
            }
        }
    }

    public bool CanRefreshEmail => !IsEmailSyncing;

    public TaskItem CreateEmptyTask()
    {
        var task = CreateTask("New task", "manual");
        AddDefaultNotesResource(task);
        AddTask(task);
        SelectedTask = task;
        TasksView.Refresh();
        return task;
    }

    public TaskItem CreateTaskFromEmail(EmailMessage email)
    {
        var task = CreateTask(email.Subject, "email, outlook");
        AddEmailResourceCore(task, email);
        task.Actions.Add(new ActionItem { ActionText = "Review the email and define the next step" });
        TaskDetailViewModel.RenumberActions(task.Actions);

        AddTask(task);
        SelectedTask = task;
        TasksView.Refresh();
        return task;
    }

    public ResourceItem AddEmailResource(TaskItem task, EmailMessage email)
    {
        var resource = AddEmailResourceCore(task, email);
        task.UpdatedOn = DateTime.Now;
        SelectedTask = task;
        RefreshEmailTaskAssociations();
        TasksView.Refresh();
        return resource;
    }

    public void FilterTasksByEmail(EmailMessage email)
    {
        SetTaskEmailFilter(email);
        IsTaskSectionExpanded = true;
        TasksView.Refresh();
        SelectedTask = TasksView.Cast<TaskItem>().FirstOrDefault();
    }

    public void ClearTaskEmailFilter()
    {
        SetTaskEmailFilter(null);
        TasksView.Refresh();
        SelectedTask = TasksView.Cast<TaskItem>().FirstOrDefault();
    }

    public void SetTaskStatus(TaskItem task, TaskItemStatus status, string statusMessage = "")
    {
        task.SetStatus(status, statusMessage);
        UpdateTaskTimeSpent(task);
        TasksView.Refresh();
        RefreshTaskBuckets();
    }

    public void ActivateTask(TaskItem task)
    {
        SetTaskStatus(task, TaskItemStatus.Active);
    }

    public void MoveTask(TaskItem task, TaskItem? targetTask, bool insertAfter)
    {
        var oldIndex = Tasks.IndexOf(task);
        if (oldIndex < 0)
        {
            return;
        }

        var newIndex = targetTask is null
            ? Tasks.Count - 1
            : Tasks.IndexOf(targetTask);
        if (newIndex < 0)
        {
            return;
        }

        if (insertAfter && targetTask is not null)
        {
            newIndex++;
        }

        if (oldIndex < newIndex)
        {
            newIndex--;
        }

        newIndex = Math.Clamp(newIndex, 0, Tasks.Count - 1);
        if (oldIndex == newIndex)
        {
            return;
        }

        Tasks.Move(oldIndex, newIndex);
        NormalizeTaskSortOrder();
        SelectedTask = task;
        TasksView.Refresh();
        RefreshTaskBuckets();
    }

    public bool IsTaskVisibleInBucket(TaskBucketViewModel bucket, TaskItem task)
    {
        return bucket.Tasks.Contains(task);
    }

    public void ReloadTasksFromStore()
    {
        FlushPendingTaskSaves();
        LoadTasksFromStore();
        RefreshEmailTaskAssociations();
        RefreshTaskBuckets();
        TasksView.Refresh();
    }

    public void FlushPendingTaskSaves()
    {
        foreach (var task in Tasks.Where(task => task.Status == TaskItemStatus.Active))
        {
            pendingTaskSaves.Add(task);
        }

        taskPersistenceTimer.Stop();
        SavePendingTasks();
    }

    public bool SaveAllTasksToStore()
    {
        if (!taskStore.IsConfigured)
        {
            return false;
        }

        try
        {
            foreach (var task in Tasks)
            {
                UpdateTaskTimeSpent(task);
                taskStore.SaveTask(task);
            }

            pendingTaskSaves.Clear();
            TaskPersistenceStatus = $"Saved {Tasks.Count} task{(Tasks.Count == 1 ? string.Empty : "s")} to SQL Server.";
            return true;
        }
        catch (Exception ex)
        {
            TaskPersistenceStatus = $"SQL Server save failed: {ex.Message}";
            return false;
        }
    }

    public async Task InitializeEmailAsync(Window owner)
    {
        await RefreshEmailsAsync(owner, allowInteractiveSignIn: false);
    }

    public async Task RefreshEmailsAsync(Window? owner, bool allowInteractiveSignIn)
    {
        if (IsEmailSyncing)
        {
            return;
        }

        IsEmailSyncing = true;
        EmailSyncStatus = GetSyncStartMessage();

        try
        {
            var result = await emailProvider.RefreshInboxAsync(
                Settings.Email,
                allowInteractiveSignIn,
                owner,
                CancellationToken.None);
            ReplaceEmails(result.Messages);
            EmailSyncStatus = result.StatusMessage;
        }
        finally
        {
            IsEmailSyncing = false;
            UpdateEmailSyncInterval();
        }
    }

    public void LoadCachedEmails()
    {
        var messages = emailProvider.LoadCachedMessages();
        ReplaceEmails(messages);
        EmailSyncStatus = messages.Count > 0 ? $"Loaded {messages.Count} cached emails." : GetEmptyEmailStatus();
    }

    private void LoadTasksFromStore()
    {
        pendingTaskSaves.Clear();
        taskPersistenceTimer.Stop();
        Tasks.Clear();
        SelectedTask = null;

        if (!taskStore.IsConfigured)
        {
            nextTaskId = 1001;
            TaskPersistenceStatus = "SQL Server storage is not configured.";
            RefreshTaskBuckets();
            return;
        }

        try
        {
            var tasks = taskStore.LoadTasks();
            foreach (var task in tasks)
            {
                RegisterTask(task);
                Tasks.Add(task);
            }

            nextTaskId = Tasks.Count == 0 ? 1001 : Tasks.Max(task => task.Id) + 1;
            SelectedTask = Tasks.FirstOrDefault();
            TaskPersistenceStatus = $"Loaded {Tasks.Count} task{(Tasks.Count == 1 ? string.Empty : "s")} from SQL Server.";
            RefreshTaskBuckets();
        }
        catch (Exception ex)
        {
            nextTaskId = 1001;
            TaskPersistenceStatus = $"SQL Server storage unavailable: {ex.Message}";
            RefreshTaskBuckets();
        }
    }

    public async Task ImportEmailFilesAsync(IEnumerable<string> filePaths)
    {
        var importedMessages = await emailImportService.ImportAsync(filePaths, CancellationToken.None);
        if (importedMessages.Count == 0)
        {
            EmailSyncStatus = "No .eml or .msg emails were imported.";
            return;
        }

        MergeEmails(importedMessages);
        emailCacheStore.Save(Emails);
        EmailSyncStatus = $"Imported {importedMessages.Count} email file{(importedMessages.Count == 1 ? string.Empty : "s")}.";
    }

    public void UpdateEmailSyncInterval()
    {
        emailSyncTimer.Interval = TimeSpan.FromMinutes(Settings.Email.SyncIntervalMinutes);
    }

    private void QueueTaskSave(TaskItem task)
    {
        if (!taskStore.IsConfigured)
        {
            return;
        }

        pendingTaskSaves.Add(task);
        taskPersistenceTimer.Stop();
        taskPersistenceTimer.Start();
    }

    private void SavePendingTasks()
    {
        taskPersistenceTimer.Stop();
        if (pendingTaskSaves.Count == 0 || !taskStore.IsConfigured)
        {
            pendingTaskSaves.Clear();
            return;
        }

        var tasksToSave = pendingTaskSaves.ToList();
        pendingTaskSaves.Clear();

        try
        {
            foreach (var task in tasksToSave)
            {
                taskStore.SaveTask(task);
            }

            TaskPersistenceStatus = $"Saved {tasksToSave.Count} task{(tasksToSave.Count == 1 ? string.Empty : "s")} to SQL Server.";
        }
        catch (Exception ex)
        {
            foreach (var task in tasksToSave)
            {
                pendingTaskSaves.Add(task);
            }

            TaskPersistenceStatus = $"SQL Server save failed: {ex.Message}";
        }
    }

    private void ReplaceEmails(IEnumerable<EmailMessage> messages)
    {
        var previousSelectedId = SelectedEmail?.Id;
        Emails.Clear();

        foreach (var message in messages)
        {
            Emails.Add(message);
        }

        RefreshEmailTaskAssociations();
        SelectedEmail = Emails.FirstOrDefault(email =>
                            !string.IsNullOrWhiteSpace(previousSelectedId)
                            && string.Equals(email.Id, previousSelectedId, StringComparison.OrdinalIgnoreCase))
                        ?? Emails.FirstOrDefault();
        EmailsView.Refresh();
    }

    private void MergeEmails(IEnumerable<EmailMessage> messages)
    {
        var byId = Emails
            .Where(email => !string.IsNullOrWhiteSpace(email.Id))
            .ToDictionary(email => email.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var message in messages)
        {
            if (!string.IsNullOrWhiteSpace(message.Id) && byId.ContainsKey(message.Id))
            {
                continue;
            }

            Emails.Add(message);
            if (!string.IsNullOrWhiteSpace(message.Id))
            {
                byId[message.Id] = message;
            }
        }

        var ordered = Emails.OrderByDescending(email => email.ReceivedOn).ToList();
        Emails.Clear();
        foreach (var email in ordered)
        {
            Emails.Add(email);
        }

        RefreshEmailTaskAssociations();
        SelectedEmail ??= Emails.FirstOrDefault();
        EmailsView.Refresh();
    }

    private string GetSyncStartMessage()
    {
        return Settings.Email.Source switch
        {
            EmailSource.MicrosoftGraph => Settings.Email.IsConfigured
                ? "Syncing Microsoft Graph Outlook..."
                : "Configure a Microsoft Entra client ID to sync Outlook with Graph.",
            EmailSource.ClassicOutlook => "Syncing Classic Outlook...",
            EmailSource.ManualImport => "Manual import selected.",
            _ => "Syncing email..."
        };
    }

    private string GetEmptyEmailStatus()
    {
        return Settings.Email.Source switch
        {
            EmailSource.MicrosoftGraph => "Configure a Microsoft Entra client ID to sync Outlook with Graph.",
            EmailSource.ClassicOutlook => "Classic Outlook selected. Refresh to read the local Outlook Inbox.",
            EmailSource.ManualImport => "Manual import selected. Import .eml or .msg files.",
            _ => "No emails loaded."
        };
    }

    private TaskItem CreateTask(string title, string tags)
    {
        var now = DateTime.Now;
        return new TaskItem
        {
            Id = nextTaskId++,
            Title = title,
            Tags = tags,
            SortOrder = Tasks.Count == 0 ? 0 : Tasks.Max(task => task.SortOrder) + 1,
            CreatedOn = now,
            UpdatedOn = now,
            TimeSpent = TimeSpan.Zero
        };
    }

    private void AddTask(TaskItem task)
    {
        RegisterTask(task);
        task.AddActivity("Task created with status Inactive.");
        UpdateTaskTimeSpent(task);
        Tasks.Add(task);
        RefreshEmailTaskAssociations();
        RefreshTaskBuckets();
        QueueTaskSave(task);
    }

    private static void AddDefaultNotesResource(TaskItem task)
    {
        task.Resources.Add(new ResourceItem
        {
            Name = "Notes",
            Kind = ResourceKind.Text,
            Content = string.Empty
        });
    }

    private void RegisterTask(TaskItem task)
    {
        task.PropertyChanged += Task_PropertyChanged;
        task.Resources.CollectionChanged += TaskResources_CollectionChanged;
        task.Actions.CollectionChanged += TaskActions_CollectionChanged;
        task.Activities.CollectionChanged += TaskActivities_CollectionChanged;

        foreach (var resource in task.Resources)
        {
            resource.PropertyChanged += Resource_PropertyChanged;
        }

        foreach (var action in task.Actions)
        {
            action.PropertyChanged += Action_PropertyChanged;
        }
    }

    private void SetTaskEmailFilter(EmailMessage? email)
    {
        if (ReferenceEquals(taskEmailFilter, email))
        {
            return;
        }

        taskEmailFilter = email;
        OnPropertyChanged(nameof(TaskEmailFilterSummary));
        OnPropertyChanged(nameof(HasTaskEmailFilter));
    }

    private static ResourceItem AddEmailResourceCore(TaskItem task, EmailMessage email)
    {
        if (!string.IsNullOrWhiteSpace(email.Id))
        {
            var existing = task.Resources.FirstOrDefault(resource =>
                resource.Kind == ResourceKind.Email
                && string.Equals(resource.EmailMessageId, email.Id, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                return existing;
            }
        }

        var resource = new ResourceItem
        {
            Name = string.IsNullOrWhiteSpace(email.Subject) ? "Email" : email.Subject,
            Kind = ResourceKind.Email,
            Content = email.Body,
            EmailMessageId = email.Id,
            EmailFrom = email.From,
            EmailSubject = email.Subject,
            EmailReceivedOn = email.ReceivedOn
        };

        task.Resources.Add(resource);
        return resource;
    }

    private bool FilterEmail(object item)
    {
        if (string.IsNullOrWhiteSpace(EmailSearchText))
        {
            return true;
        }

        if (item is not EmailMessage email)
        {
            return false;
        }

        return Contains(email.From, EmailSearchText)
               || Contains(email.Subject, EmailSearchText)
               || Contains(email.Preview, EmailSearchText)
               || Contains(email.Body, EmailSearchText);
    }

    private bool FilterTask(object item)
    {
        if (item is not TaskItem task)
        {
            return false;
        }

        if (taskEmailFilter is not null && !TaskHasEmailResource(task, taskEmailFilter))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(TaskSearchText))
        {
            return true;
        }

        return task.Id.ToString().Contains(TaskSearchText, StringComparison.OrdinalIgnoreCase)
               || Contains(task.Title, TaskSearchText)
               || Contains(task.Tags, TaskSearchText)
               || Contains(task.Status.ToString(), TaskSearchText);
    }

    private void RefreshTaskBuckets()
    {
        var visibleTasks = TasksView
            .Cast<TaskItem>()
            .OrderBy(task => task.SortOrder)
            .ThenBy(task => task.Id)
            .ToList();

        TagTaskBuckets.Clear();
        foreach (var tagGroup in visibleTasks
                     .SelectMany(task => SplitTags(task.Tags).Select(tag => (Tag: tag, Task: task)))
                     .GroupBy(item => item.Tag, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            TagTaskBuckets.Add(new TaskBucketViewModel(
                tagGroup.Key,
                tagGroup.Key,
                tagGroup.Select(item => item.Task)
                    .Distinct()
                    .OrderBy(task => task.SortOrder)
                    .ThenBy(task => task.Id)));
        }

        StatusTaskBuckets.Clear();
        foreach (var status in Enum.GetValues<TaskItemStatus>())
        {
            StatusTaskBuckets.Add(new TaskBucketViewModel(
                FormatStatus(status),
                status,
                visibleTasks.Where(task => task.Status == status)));
        }
    }

    private void NormalizeTaskSortOrder()
    {
        for (var index = 0; index < Tasks.Count; index++)
        {
            var task = Tasks[index];
            if (task.SortOrder != index)
            {
                task.SortOrder = index;
            }

            QueueTaskSave(task);
        }
    }

    private static IEnumerable<string> SplitTags(string tags)
    {
        return tags
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string FormatStatus(TaskItemStatus status)
    {
        return status switch
        {
            TaskItemStatus.InProgress => "In-Progress",
            TaskItemStatus.OnHold => "On Hold",
            _ => status.ToString()
        };
    }

    private static bool Contains(string value, string query)
    {
        return value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void Task_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TaskItem task)
        {
            return;
        }

        if (e.PropertyName is nameof(TaskItem.Title) or nameof(TaskItem.Tags) or nameof(TaskItem.SortOrder))
        {
            RefreshEmailTaskAssociations();
            TasksView.Refresh();
            RefreshTaskBuckets();
        }

        if (e.PropertyName == nameof(TaskItem.Status))
        {
            if (!isEnforcingActiveTask && task.Status == TaskItemStatus.Active)
            {
                EnforceSingleActiveTask(task);
            }

            UpdateTaskTimeSpent(task);
            TasksView.Refresh();
            RefreshTaskBuckets();
        }

        if (e.PropertyName != nameof(TaskItem.TimeSpent))
        {
            QueueTaskSave(task);
        }
    }

    private void TaskResources_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (FindTaskForResourceCollection(sender) is { } task)
        {
            if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace
                && e.NewItems is not null)
            {
                foreach (ResourceItem resource in e.NewItems)
                {
                    resource.PropertyChanged += Resource_PropertyChanged;
                }
            }

            task.UpdatedOn = DateTime.Now;
            QueueTaskSave(task);
        }

        RefreshEmailTaskAssociations();
        TasksView.Refresh();
    }

    private void TaskActions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (FindTaskForActionCollection(sender) is not { } task)
        {
            return;
        }

        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace
            && e.NewItems is not null)
        {
            foreach (ActionItem action in e.NewItems)
            {
                action.PropertyChanged += Action_PropertyChanged;
            }
        }

        task.UpdatedOn = DateTime.Now;
        QueueTaskSave(task);
    }

    private void TaskActivities_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (FindTaskForActivityCollection(sender) is { } task)
        {
            QueueTaskSave(task);
        }
    }

    private void Resource_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ResourceItem resource
            || Tasks.FirstOrDefault(task => task.Resources.Contains(resource)) is not { } task)
        {
            return;
        }

        task.UpdatedOn = DateTime.Now;
        RefreshEmailTaskAssociations();
        QueueTaskSave(task);
    }

    private void Action_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ActionItem action
            || Tasks.FirstOrDefault(task => task.Actions.Contains(action)) is not { } task)
        {
            return;
        }

        task.UpdatedOn = DateTime.Now;
        QueueTaskSave(task);
    }

    private TaskItem? FindTaskForResourceCollection(object? sender)
    {
        return Tasks.FirstOrDefault(task => ReferenceEquals(task.Resources, sender));
    }

    private TaskItem? FindTaskForActionCollection(object? sender)
    {
        return Tasks.FirstOrDefault(task => ReferenceEquals(task.Actions, sender));
    }

    private TaskItem? FindTaskForActivityCollection(object? sender)
    {
        return Tasks.FirstOrDefault(task => ReferenceEquals(task.Activities, sender));
    }

    private void RefreshEmailTaskAssociations()
    {
        foreach (var email in Emails)
        {
            var taskNames = Tasks
                .Where(task => TaskHasEmailResource(task, email))
                .Select(task => task.Title)
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            email.AssociatedTaskNames = string.Join(", ", taskNames);
        }
    }

    private static bool TaskHasEmailResource(TaskItem task, EmailMessage email)
    {
        return task.Resources.Any(resource =>
            resource.Kind == ResourceKind.Email
            && IsEmailResourceMatch(resource, email));
    }

    private static bool IsEmailResourceMatch(ResourceItem resource, EmailMessage email)
    {
        if (!string.IsNullOrWhiteSpace(email.Id)
            && string.Equals(resource.EmailMessageId, email.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(email.Id)
               && string.Equals(resource.EmailSubject, email.Subject, StringComparison.OrdinalIgnoreCase)
               && string.Equals(resource.EmailFrom, email.From, StringComparison.OrdinalIgnoreCase)
               && resource.EmailReceivedOn == email.ReceivedOn;
    }

    private void EnforceSingleActiveTask(TaskItem activeTask)
    {
        isEnforcingActiveTask = true;

        try
        {
            foreach (var task in Tasks.Where(task => !ReferenceEquals(task, activeTask) && task.Status == TaskItemStatus.Active).ToList())
            {
                var restoreStatus = task.StatusBeforeActive == TaskItemStatus.Active
                    ? TaskItemStatus.Inactive
                    : task.StatusBeforeActive;

                task.SetStatus(restoreStatus);
                UpdateTaskTimeSpent(task);
            }
        }
        finally
        {
            isEnforcingActiveTask = false;
        }
    }

    private void RecalculateAllTaskTimeSpent()
    {
        var now = DateTime.Now;
        foreach (var task in Tasks)
        {
            UpdateTaskTimeSpent(task, now);
        }
    }

    private void UpdateActiveTaskTimeSpent()
    {
        if (Tasks.FirstOrDefault(task => task.Status == TaskItemStatus.Active) is not { } activeTask)
        {
            return;
        }

        UpdateTaskTimeSpent(activeTask);
    }

    private static void UpdateTaskTimeSpent(TaskItem task)
    {
        UpdateTaskTimeSpent(task, DateTime.Now);
    }

    private static void UpdateTaskTimeSpent(TaskItem task, DateTime now)
    {
        task.TimeSpent = CalculateTimeSpent(task, now);
    }

    private static TimeSpan CalculateTimeSpent(TaskItem task, DateTime now)
    {
        var total = TimeSpan.Zero;
        DateTime? activeStartedOn = null;

        foreach (var activity in task.Activities
                     .Where(activity => activity.FromStatus.HasValue || activity.ToStatus.HasValue)
                     .OrderBy(activity => activity.OccurredOn))
        {
            if (activity.ToStatus == TaskItemStatus.Active)
            {
                activeStartedOn = activity.OccurredOn;
            }
            else if (activity.FromStatus == TaskItemStatus.Active && activeStartedOn.HasValue)
            {
                total += activity.OccurredOn - activeStartedOn.Value;
                activeStartedOn = null;
            }
        }

        if (task.Status == TaskItemStatus.Active && activeStartedOn.HasValue)
        {
            total += now - activeStartedOn.Value;
        }

        return TimeSpan.FromSeconds(Math.Max(0, Math.Floor(total.TotalSeconds)));
    }
}
