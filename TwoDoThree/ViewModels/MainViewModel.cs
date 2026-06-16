using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Threading;
using System.Windows.Data;
using TwoDoThree.Models;
using TaskItemStatus = TwoDoThree.Models.TaskStatus;

namespace TwoDoThree.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly List<EmailMessage> configuredAccountMessages = new();
    private readonly DispatcherTimer activeTaskTimer;
    private bool isEnforcingActiveTask;
    private int nextTaskId = 1004;
    private string searchText = string.Empty;
    private EmailMessage? taskEmailFilter;
    private EmailMessage? selectedEmail;
    private TaskItem? selectedTask;
    private bool isEmailSectionExpanded = true;
    private bool isTaskSectionExpanded = true;

    public MainViewModel()
    {
        Settings = new AppSettings();
        EmailsView = CollectionViewSource.GetDefaultView(Emails);
        TasksView = CollectionViewSource.GetDefaultView(Tasks);
        EmailsView.Filter = FilterEmail;
        TasksView.Filter = FilterTask;

        SeedInbox();
        RefreshEmails();
        SeedTasks();
        RefreshEmailTaskAssociations();
        RecalculateAllTaskTimeSpent();

        activeTaskTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        activeTaskTimer.Tick += (_, _) => UpdateActiveTaskTimeSpent();
        activeTaskTimer.Start();
    }

    public AppSettings Settings { get; }

    public ObservableCollection<EmailMessage> Emails { get; } = new();

    public ObservableCollection<TaskItem> Tasks { get; } = new();

    public ICollectionView EmailsView { get; }

    public ICollectionView TasksView { get; }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
            {
                EmailsView.Refresh();
                TasksView.Refresh();
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

    public void SetTaskStatus(TaskItem task, TaskItemStatus status)
    {
        task.Status = status;
        UpdateTaskTimeSpent(task);
        TasksView.Refresh();
    }

    public void ActivateTask(TaskItem task)
    {
        SetTaskStatus(task, TaskItemStatus.Active);
    }

    public void RefreshEmails()
    {
        Emails.Clear();

        if (Settings.Email.IsConfigured)
        {
            foreach (var message in configuredAccountMessages)
            {
                Emails.Add(message);
            }
        }

        SelectedEmail = Emails.FirstOrDefault();
        EmailsView.Refresh();
    }

    private TaskItem CreateTask(string title, string tags)
    {
        var now = DateTime.Now;
        return new TaskItem
        {
            Id = nextTaskId++,
            Title = title,
            Tags = tags,
            CreatedOn = now,
            UpdatedOn = now,
            TimeSpent = TimeSpan.Zero
        };
    }

    private void AddTask(TaskItem task)
    {
        task.PropertyChanged += Task_PropertyChanged;
        task.Resources.CollectionChanged += TaskResources_CollectionChanged;
        task.AddActivity("Task created with status Inactive.");
        UpdateTaskTimeSpent(task);
        Tasks.Add(task);
        RefreshEmailTaskAssociations();
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
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        if (item is not EmailMessage email)
        {
            return false;
        }

        return Contains(email.From, SearchText)
               || Contains(email.Subject, SearchText)
               || Contains(email.Preview, SearchText)
               || Contains(email.Body, SearchText);
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

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        return task.Id.ToString().Contains(SearchText, StringComparison.OrdinalIgnoreCase)
               || Contains(task.Title, SearchText)
               || Contains(task.Tags, SearchText)
               || Contains(task.Status.ToString(), SearchText);
    }

    private static bool Contains(string value, string query)
    {
        return value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void SeedInbox()
    {
        configuredAccountMessages.AddRange(
        [
            new EmailMessage
            {
                Id = "em-001",
                From = "Maya Rodriguez",
                Subject = "Quarterly planning notes",
                ReceivedOn = DateTime.Now.AddHours(-4),
                Preview = "Attaching the outline we discussed for next quarter.",
                Body = "Attaching the outline we discussed for next quarter. The main decision is whether to split the migration work into two phases or keep it behind a single launch date."
            },
            new EmailMessage
            {
                Id = "em-002",
                From = "Alex Chen",
                Subject = "Follow-up: onboarding checklist",
                ReceivedOn = DateTime.Now.AddDays(-1).AddMinutes(-20),
                Preview = "Can you turn the remaining onboarding items into trackable tasks?",
                Body = "Can you turn the remaining onboarding items into trackable tasks? The highest priority items are access review, first-week walkthrough, and the support escalation map."
            },
            new EmailMessage
            {
                Id = "em-003",
                From = "Nina Patel",
                Subject = "Screenshot for image resource test",
                ReceivedOn = DateTime.Now.AddDays(-2),
                Preview = "This one may be useful as a resource once image importing is wired.",
                Body = "This one may be useful as a resource once image importing is wired. The design review notes are inline below the image in the original thread."
            }
        ]);
    }

    private void SeedTasks()
    {
        var task = CreateTask("Prepare onboarding task flow", "onboarding, process");
        task.Resources.Add(new ResourceItem
        {
            Name = "Initial notes",
            Kind = ResourceKind.Text,
            Content = "Capture the workflow, split it into actions, and attach source material as resources."
        });
        task.Resources.Add(new ResourceItem
        {
            Name = "Status helper",
            Kind = ResourceKind.CodeSnippet,
            Content = "status switch { NotStarted => white, InProgress => blue, Completed => green, Failed => red, Cancelled => grey }"
        });
        task.Actions.Add(new ActionItem { ActionText = "Draft task structure" });
        task.Actions.Add(new ActionItem { ActionText = "Add resource viewer", IndentLevel = 1, Status = ActionStatus.InProgress });
        task.Actions.Add(new ActionItem { ActionText = "Verify action numbering", Status = ActionStatus.NotStarted });
        TaskDetailViewModel.RenumberActions(task.Actions);
        AddTask(task);
        SetTaskStatus(task, TaskItemStatus.InProgress);
        SelectedTask = task;
    }

    private void Task_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TaskItem task)
        {
            return;
        }

        if (e.PropertyName == nameof(TaskItem.Title))
        {
            RefreshEmailTaskAssociations();
            TasksView.Refresh();
            return;
        }

        if (e.PropertyName != nameof(TaskItem.Status))
        {
            return;
        }

        if (!isEnforcingActiveTask && task.Status == TaskItemStatus.Active)
        {
            EnforceSingleActiveTask(task);
        }

        UpdateTaskTimeSpent(task);
        TasksView.Refresh();
    }

    private void TaskResources_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshEmailTaskAssociations();
        TasksView.Refresh();
    }

    private void RefreshEmailTaskAssociations()
    {
        foreach (var email in configuredAccountMessages)
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

                task.Status = restoreStatus;
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
