using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using TwoDoThree.Models;

namespace TwoDoThree.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly List<EmailMessage> configuredAccountMessages = new();
    private int nextTaskId = 1004;
    private string searchText = string.Empty;
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

    public TaskItem CreateEmptyTask()
    {
        var task = CreateTask("New task", "manual");
        Tasks.Add(task);
        SelectedTask = task;
        TasksView.Refresh();
        return task;
    }

    public TaskItem CreateTaskFromEmail(EmailMessage email)
    {
        var task = CreateTask(email.Subject, "email, outlook");
        task.Resources.Add(new ResourceItem
        {
            Name = email.Subject,
            Kind = ResourceKind.Text,
            Content = $"From: {email.From}{Environment.NewLine}Received: {email.ReceivedOn:g}{Environment.NewLine}{Environment.NewLine}{email.Body}"
        });
        task.Actions.Add(new ActionItem { ActionText = "Review the email and define the next step" });
        TaskDetailViewModel.RenumberActions(task.Actions);

        Tasks.Add(task);
        SelectedTask = task;
        TasksView.Refresh();
        return task;
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
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        if (item is not TaskItem task)
        {
            return false;
        }

        return task.Id.ToString().Contains(SearchText, StringComparison.OrdinalIgnoreCase)
               || Contains(task.Title, SearchText)
               || Contains(task.Tags, SearchText);
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
        Tasks.Add(task);
        SelectedTask = task;
    }
}
