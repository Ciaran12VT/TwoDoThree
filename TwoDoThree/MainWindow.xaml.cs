using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using TwoDoThree.Models;
using TwoDoThree.Services;
using TwoDoThree.ViewModels;
using TwoDoThree.Views;
using TaskItemStatus = TwoDoThree.Models.TaskStatus;

namespace TwoDoThree;

public partial class MainWindow : Window
{
    private const string EmailDataFormat = "TwoDoThree.EmailMessage";
    private const string TaskDataFormat = "TwoDoThree.TaskItem";
    private const double ExpanderGlyphWidth = 24;

    private static readonly IReadOnlyList<TaskListColumnOption> TaskListColumnOptions =
    [
        new(TaskListColumn.Id, "ID"),
        new(TaskListColumn.Title, "Title"),
        new(TaskListColumn.Tags, "Tags"),
        new(TaskListColumn.Pocs, "POCs"),
        new(TaskListColumn.Status, "Status"),
        new(TaskListColumn.DueBy, "Due By"),
        new(TaskListColumn.CreatedOn, "CreatedOn"),
        new(TaskListColumn.UpdatedOn, "UpdatedOn"),
        new(TaskListColumn.TimeSpent, "Time Spent")
    ];

    private readonly List<TaskDetailWindow> openTaskDetailWindows = new();
    private readonly IAppSettingsStore settingsStore;
    private readonly IEmailCacheStore emailCacheStore;
    private readonly IGraphAuthService graphAuthService;
    private readonly ISurf2IntegrationService surf2IntegrationService;
    private readonly ISurf2Launcher surf2Launcher;

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        settingsStore = new JsonAppSettingsStore();
        emailCacheStore = new JsonEmailCacheStore();
        graphAuthService = new GraphAuthService();
        surf2IntegrationService = new Surf2IntegrationService();
        surf2Launcher = new Surf2Launcher();
        var graphMailClient = new GraphMailClient(new HttpClient());
        var graphProvider = new GraphEmailProvider(graphAuthService, graphMailClient, emailCacheStore);
        var classicProvider = new ClassicOutlookEmailProvider(emailCacheStore);
        var emailProvider = new CompositeEmailProvider(graphProvider, classicProvider, emailCacheStore);
        var emailImportService = new EmailImportService();
        var settings = settingsStore.Load();
        var taskStore = new SqlServerTaskStore(settings.Database);
        DataContext = new MainViewModel(settings, emailProvider, emailImportService, emailCacheStore, taskStore);
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyTaskGridColumnVisibility();
        await ViewModel.InitializeEmailAsync(this);
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var originalClientId = ViewModel.Settings.Email.ClientId;
        var originalTenantId = ViewModel.Settings.Email.TenantId;
        var originalConnectionString = ViewModel.Settings.Database.ConnectionString;
        var originalSurf2Enabled = ViewModel.Settings.Surf2.IsEnabled;
        var originalSurf2ConnectionString = ViewModel.Settings.Surf2.ConnectionString;
        var originalSurf2ExecutablePath = ViewModel.Settings.Surf2.ExecutablePath;
        var window = new SettingsWindow(
            ViewModel.Settings,
            settingsStore,
            graphAuthService,
            emailCacheStore)
        {
            Owner = this
        };

        if (window.ShowDialog() == true)
        {
            if (!string.Equals(originalClientId, ViewModel.Settings.Email.ClientId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(originalTenantId, ViewModel.Settings.Email.TenantId, StringComparison.OrdinalIgnoreCase))
            {
                await graphAuthService.ClearCacheAsync(CancellationToken.None);
                emailCacheStore.Clear();
                ViewModel.LoadCachedEmails();
            }

            settingsStore.Save(ViewModel.Settings);
            if (!string.Equals(originalConnectionString, ViewModel.Settings.Database.ConnectionString, StringComparison.Ordinal))
            {
                var shouldReloadTasks = true;
                if (string.IsNullOrWhiteSpace(originalConnectionString)
                    && !string.IsNullOrWhiteSpace(ViewModel.Settings.Database.ConnectionString))
                {
                    shouldReloadTasks = ViewModel.SaveAllTasksToStore();
                }

                if (shouldReloadTasks)
                {
                    ViewModel.ReloadTasksFromStore();
                }
            }

            if (originalSurf2Enabled != ViewModel.Settings.Surf2.IsEnabled ||
                !string.Equals(originalSurf2ConnectionString, ViewModel.Settings.Surf2.ConnectionString, StringComparison.Ordinal) ||
                !string.Equals(originalSurf2ExecutablePath, ViewModel.Settings.Surf2.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                await RefreshOpenTaskDetailSurfScopesAsync();
            }

            ViewModel.UpdateEmailSyncInterval();
            await ViewModel.RefreshEmailsAsync(this, allowInteractiveSignIn: false);
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        ViewModel.FlushPendingTaskSaves();
    }

    private async Task RefreshOpenTaskDetailSurfScopesAsync()
    {
        openTaskDetailWindows.RemoveAll(window => !window.IsVisible);
        foreach (TaskDetailWindow window in openTaskDetailWindows)
        {
            if (window.DataContext is TaskDetailViewModel viewModel)
            {
                await viewModel.InitializeSurf2Async();
            }
        }
    }

    private async void RefreshEmailsButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshEmailsAsync(this, allowInteractiveSignIn: true);
    }

    private async void ImportEmailsButton_Click(object sender, RoutedEventArgs e)
    {
        await ImportEmailFilesFromDialogAsync();
    }

    private void AddTaskButton_Click(object sender, RoutedEventArgs e)
    {
        OpenTaskDetail(ViewModel.CreateEmptyTask(), activate: false);
    }

    private void CreateTaskFromEmailMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedEmail is EmailMessage email)
        {
            OpenTaskDetail(ViewModel.CreateTaskFromEmail(email), activate: false);
        }
    }

    private async void ImportEmailFilesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await ImportEmailFilesFromDialogAsync();
    }

    private async Task ImportEmailFilesFromDialogAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import email files",
            Filter = "Email files|*.eml;*.msg|All files|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            await ViewModel.ImportEmailFilesAsync(dialog.FileNames);
        }
    }

    private void EmailListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not { DataContext: EmailMessage email } item)
        {
            return;
        }

        item.IsSelected = true;
        var data = new DataObject();
        data.SetData(EmailDataFormat, email);
        data.SetText(email.Subject);
        DragDrop.DoDragDrop(item, data, DragDropEffects.Copy);
    }

    private void EmailListBox_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasDroppedEmailFiles(e)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void EmailListBox_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetDroppedEmailFiles(e, out var files, out var temporaryFiles))
        {
            return;
        }

        try
        {
            await ViewModel.ImportEmailFilesAsync(files);
            e.Handled = true;
        }
        finally
        {
            DeleteTemporaryFiles(temporaryFiles);
        }
    }

    private void EmailListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { } item)
        {
            item.IsSelected = true;
        }
    }

    private void EmailListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not { DataContext: EmailMessage email } item)
        {
            return;
        }

        item.IsSelected = true;
        ViewModel.SelectedEmail = email;
        ViewModel.FilterTasksByEmail(email);
        e.Handled = true;
    }

    private void ClearTaskEmailFilterButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearTaskEmailFilter();
    }

    private void TaskFilterSetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel)
        {
            settingsStore.Save(ViewModel.Settings);
        }
    }

    private void TaskFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        var currentFilterSet = ViewModel.SelectedFilterSet is { Id.Length: > 0 } filterSet
            ? filterSet
            : null;
        var window = new TaskFilterWindow(currentFilterSet)
        {
            Owner = this
        };

        if (window.ShowDialog() == true)
        {
            ViewModel.SaveFilterSet(window.FilterSet);
            settingsStore.Save(ViewModel.Settings);
        }
    }

    private void TasksListOptionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement button)
        {
            return;
        }

        var menu = new ContextMenu();
        AddVisibleColumnsMenu(menu);
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Copy as Text", CopyTasksAsText);
        AddMenuItem(menu, "Copy as CSV", CopyTasksAsCsv);
        AddMenuItem(menu, "Export to CSV", ExportTasksAsCsv);

        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void TasksGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TasksGrid.SelectedItem is TaskItem task)
        {
            OpenTaskDetail(task, activate: !HasOpenTaskDetailWindows);
        }
    }

    private void TaskBoardCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2
            && sender is FrameworkElement { DataContext: TaskItem task })
        {
            ViewModel.SelectedTask = task;
            OpenTaskDetail(task, activate: !HasOpenTaskDetailWindows);
            e.Handled = true;
        }
    }

    private void TaskBoardCard_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed
            || sender is not FrameworkElement { DataContext: TaskItem task } card)
        {
            return;
        }

        ViewModel.SelectedTask = task;
        var data = new DataObject();
        data.SetData(TaskDataFormat, task);
        DragDrop.DoDragDrop(card, data, DragDropEffects.Move);
    }

    private void TagTaskBucket_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDraggedTask(e, out var task)
                    && task is not null
                    && sender is FrameworkElement { DataContext: TaskBucketViewModel bucket }
                    && ViewModel.IsTaskVisibleInBucket(bucket, task)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void TagTaskBucket_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetDraggedTask(e, out var task)
            || task is null
            || sender is not FrameworkElement { DataContext: TaskBucketViewModel bucket }
            || !ViewModel.IsTaskVisibleInBucket(bucket, task))
        {
            return;
        }

        var targetTask = GetTaskFromDropTarget(e.OriginalSource);
        if (targetTask is not null && !ViewModel.IsTaskVisibleInBucket(bucket, targetTask))
        {
            targetTask = null;
        }

        ViewModel.MoveTask(task, targetTask == task ? null : targetTask, ShouldInsertAfter(e, targetTask));
        e.Handled = true;
    }

    private void StatusTaskBucket_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDraggedTask(e, out _)
                    && sender is FrameworkElement { DataContext: TaskBucketViewModel { Key: TaskItemStatus } }
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void StatusTaskBucket_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetDraggedTask(e, out var task)
            || task is null
            || sender is not FrameworkElement { DataContext: TaskBucketViewModel { Key: TaskItemStatus status } bucket })
        {
            return;
        }

        if (task.Status != status)
        {
            if (!StatusMessageWindow.TryPrompt(this, status, out var statusMessage))
            {
                e.Handled = true;
                return;
            }

            ViewModel.SetTaskStatus(task, status, statusMessage);
        }

        var targetTask = GetTaskFromDropTarget(e.OriginalSource);
        if (targetTask is not null && !ViewModel.IsTaskVisibleInBucket(bucket, targetTask))
        {
            targetTask = null;
        }

        ViewModel.MoveTask(task, targetTask == task ? null : targetTask, ShouldInsertAfter(e, targetTask));
        e.Handled = true;
    }

    private void TasksGrid_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDraggedEmail(e, out _)
                    && FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is { DataContext: TaskItem }
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void TasksGrid_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetDraggedEmail(e, out var email)
            || email is null
            || FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is not { DataContext: TaskItem task } row)
        {
            return;
        }

        row.IsSelected = true;
        row.Focus();
        var resource = ViewModel.AddEmailResource(task, email);
        RefreshOpenTaskDetailWindows(task, resource);
        e.Handled = true;
    }

    private void TasksGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is { } row)
        {
            row.IsSelected = true;
            row.Focus();
        }
    }

    private void PeekTaskMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask is { } task)
        {
            OpenTaskDetail(task, activate: false);
        }
    }

    private void SetTaskStatusMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: TaskItemStatus status }
            && ViewModel.SelectedTask is { } task)
        {
            if (!StatusMessageWindow.TryPrompt(this, status, out var statusMessage))
            {
                return;
            }

            ViewModel.SetTaskStatus(task, status, statusMessage);
        }
    }

    private void DeleteTaskMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask is not { } task)
        {
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            this,
            "Are you sure you want to permanently delete this task?",
            "Delete task",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        if (ViewModel.DeleteTask(task))
        {
            CloseOpenTaskDetailWindows(task);
            return;
        }

        MessageBox.Show(
            this,
            ViewModel.TaskPersistenceStatus,
            "Delete task",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void HeaderFilterTextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            textBox.Focus();
        }
    }

    private void SectionExpander_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSectionHeaderWidths();
    }

    private void UpdateSectionHeaderWidths()
    {
        EmailHeaderPanel.Width = Math.Max(0, EmailExpander.ActualWidth - ExpanderGlyphWidth);
        TasksHeaderPanel.Width = Math.Max(0, TasksExpander.ActualWidth - ExpanderGlyphWidth);
    }

    private void CopyTasksAsText()
    {
        SetClipboardText(CreateTasksText(GetVisibleTasks()));
    }

    private void CopyTasksAsCsv()
    {
        SetClipboardText(CreateTasksCsv(GetVisibleTasks()));
    }

    private void ExportTasksAsCsv()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export task list",
            FileName = "tasks.csv",
            Filter = "CSV files|*.csv|All files|*.*",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, CreateTasksCsv(GetVisibleTasks()), Encoding.UTF8);
    }

    private void AddVisibleColumnsMenu(ItemsControl menu)
    {
        var columnsMenu = new MenuItem { Header = "Visible Columns" };
        foreach (var option in TaskListColumnOptions)
        {
            var item = new MenuItem
            {
                Header = option.Header,
                IsCheckable = true,
                IsChecked = ViewModel.Settings.TaskList.IsColumnVisible(option.Column),
                StaysOpenOnClick = true
            };

            item.Click += (_, _) =>
            {
                ViewModel.Settings.TaskList.SetColumnVisible(option.Column, item.IsChecked);
                ApplyTaskGridColumnVisibility();
                settingsStore.Save(ViewModel.Settings);
            };
            columnsMenu.Items.Add(item);
        }

        menu.Items.Add(columnsMenu);
    }

    private void ApplyTaskGridColumnVisibility()
    {
        foreach (var option in TaskListColumnOptions)
        {
            GetTaskGridColumn(option.Column).Visibility = ViewModel.Settings.TaskList.IsColumnVisible(option.Column)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private DataGridColumn GetTaskGridColumn(TaskListColumn column)
    {
        return column switch
        {
            TaskListColumn.Id => TaskIdColumn,
            TaskListColumn.Title => TaskTitleColumn,
            TaskListColumn.Tags => TaskTagsColumn,
            TaskListColumn.Pocs => TaskPocsColumn,
            TaskListColumn.Status => TaskStatusColumn,
            TaskListColumn.DueBy => TaskDueByColumn,
            TaskListColumn.CreatedOn => TaskCreatedOnColumn,
            TaskListColumn.UpdatedOn => TaskUpdatedOnColumn,
            TaskListColumn.TimeSpent => TaskTimeSpentColumn,
            _ => TaskTitleColumn
        };
    }

    private void OpenTaskDetail(TaskItem task, bool activate)
    {
        if (activate)
        {
            ViewModel.ActivateTask(task);
        }

        var window = new TaskDetailWindow(
            task,
            ViewModel.Settings.Tags,
            ViewModel.Settings.Surf2,
            surf2IntegrationService,
            surf2Launcher)
        {
            Owner = this
        };

        openTaskDetailWindows.Add(window);
        window.Closed += (_, _) =>
        {
            openTaskDetailWindows.Remove(window);
            ViewModel.FlushPendingTaskSaves();
            ViewModel.TasksView.Refresh();
        };

        window.Show();
    }

    private bool HasOpenTaskDetailWindows
    {
        get
        {
            openTaskDetailWindows.RemoveAll(window => !window.IsVisible);
            return openTaskDetailWindows.Count > 0;
        }
    }

    private void RefreshOpenTaskDetailWindows(TaskItem task, ResourceItem selectedResource)
    {
        openTaskDetailWindows.RemoveAll(window => !window.IsVisible);

        foreach (var window in openTaskDetailWindows)
        {
            if (window.DataContext is TaskDetailViewModel viewModel
                && ReferenceEquals(viewModel.Task, task))
            {
                viewModel.RefreshResourceGroups(selectedResource);
            }
        }
    }

    private void CloseOpenTaskDetailWindows(TaskItem task)
    {
        foreach (var window in openTaskDetailWindows.ToList())
        {
            if (window.DataContext is TaskDetailViewModel viewModel
                && ReferenceEquals(viewModel.Task, task))
            {
                window.Close();
            }
        }
    }

    private static bool TryGetDraggedEmail(DragEventArgs e, out EmailMessage? email)
    {
        email = e.Data.GetData(EmailDataFormat) as EmailMessage;
        return email is not null;
    }

    private static bool TryGetDraggedTask(DragEventArgs e, out TaskItem? task)
    {
        task = e.Data.GetData(TaskDataFormat) as TaskItem;
        return task is not null;
    }

    private static TaskItem? GetTaskFromDropTarget(object originalSource)
    {
        var current = originalSource as DependencyObject;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: TaskItem task })
            {
                return task;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static bool ShouldInsertAfter(DragEventArgs e, TaskItem? targetTask)
    {
        if (targetTask is null)
        {
            return true;
        }

        var current = e.OriginalSource as DependencyObject;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: TaskItem task } element
                && ReferenceEquals(task, targetTask))
            {
                return e.GetPosition(element).Y > element.ActualHeight / 2;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return true;
    }

    private static bool HasDroppedEmailFiles(DragEventArgs e)
    {
        return TryGetFileDropEmailFiles(e.Data, out var droppedFiles)
               && droppedFiles.Count > 0
               || HasOutlookVirtualEmailFiles(e.Data);
    }

    private static bool TryGetDroppedEmailFiles(
        DragEventArgs e,
        out IReadOnlyList<string> files,
        out IReadOnlyList<string> temporaryFiles)
    {
        temporaryFiles = [];
        if (TryGetFileDropEmailFiles(e.Data, out files))
        {
            return true;
        }

        if (TryExtractOutlookVirtualEmailFiles(e.Data, out files))
        {
            temporaryFiles = files;
            return true;
        }

        files = [];
        return false;
    }

    private static bool TryGetFileDropEmailFiles(IDataObject data, out IReadOnlyList<string> files)
    {
        files = [];
        if (!data.GetDataPresent(DataFormats.FileDrop)
            || data.GetData(DataFormats.FileDrop) is not string[] droppedFiles)
        {
            return false;
        }

        files = droppedFiles
            .Where(file => Path.GetExtension(file) is var extension
                           && (extension.Equals(".eml", StringComparison.OrdinalIgnoreCase)
                               || extension.Equals(".msg", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return files.Count > 0;
    }

    private static bool HasOutlookVirtualEmailFiles(IDataObject data)
    {
        return data.GetDataPresent("FileContents")
               && (data.GetDataPresent("FileGroupDescriptorW")
                   || data.GetDataPresent("FileGroupDescriptor"));
    }

    private static bool TryExtractOutlookVirtualEmailFiles(IDataObject data, out IReadOnlyList<string> files)
    {
        files = [];
        if (!HasOutlookVirtualEmailFiles(data)
            || !TryReadFileGroupDescriptorNames(data, out var names)
            || !TryGetFileContentStreams(data, out var streams))
        {
            return false;
        }

        var extractedFiles = new List<string>();
        var tempDirectory = Path.Combine(Path.GetTempPath(), "TwoDoThree", "EmailDrops");
        Directory.CreateDirectory(tempDirectory);
        var count = Math.Min(names.Count, streams.Count);

        for (var index = 0; index < count; index++)
        {
            var name = names[index];
            var extension = Path.GetExtension(name);
            if (!extension.Equals(".eml", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".msg", StringComparison.OrdinalIgnoreCase))
            {
                extension = ".msg";
            }

            var tempFile = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}{extension}");
            using (var output = File.Create(tempFile))
            {
                var stream = streams[index];
                if (stream.CanSeek)
                {
                    stream.Position = 0;
                }

                stream.CopyTo(output);
            }

            extractedFiles.Add(tempFile);
        }

        files = extractedFiles;
        return files.Count > 0;
    }

    private static bool TryReadFileGroupDescriptorNames(IDataObject data, out IReadOnlyList<string> names)
    {
        if (TryReadFileGroupDescriptorNames(data, "FileGroupDescriptorW", unicode: true, out names))
        {
            return true;
        }

        return TryReadFileGroupDescriptorNames(data, "FileGroupDescriptor", unicode: false, out names);
    }

    private static bool TryReadFileGroupDescriptorNames(
        IDataObject data,
        string format,
        bool unicode,
        out IReadOnlyList<string> names)
    {
        names = [];
        if (!data.GetDataPresent(format)
            || data.GetData(format) is not MemoryStream descriptorStream)
        {
            return false;
        }

        if (descriptorStream.CanSeek)
        {
            descriptorStream.Position = 0;
        }

        var descriptorSize = unicode ? 592 : 332;
        var fileNameOffset = 72;
        var fileNameBytes = unicode ? 520 : 260;
        var encoding = unicode ? Encoding.Unicode : Encoding.ASCII;
        var parsedNames = new List<string>();

        using var reader = new BinaryReader(descriptorStream, encoding, leaveOpen: true);
        var itemCount = reader.ReadInt32();
        for (var index = 0; index < itemCount; index++)
        {
            var namePosition = 4 + (index * descriptorSize) + fileNameOffset;
            if (descriptorStream.Length < namePosition + fileNameBytes)
            {
                break;
            }

            descriptorStream.Position = namePosition;
            var buffer = reader.ReadBytes(fileNameBytes);
            var fileName = encoding.GetString(buffer);
            var terminator = fileName.IndexOf('\0');
            if (terminator >= 0)
            {
                fileName = fileName[..terminator];
            }

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                parsedNames.Add(fileName.Trim());
            }
        }

        names = parsedNames;
        return names.Count > 0;
    }

    private static bool TryGetFileContentStreams(IDataObject data, out IReadOnlyList<Stream> streams)
    {
        streams = [];
        if (!data.GetDataPresent("FileContents"))
        {
            return false;
        }

        var content = data.GetData("FileContents");
        streams = content switch
        {
            Stream stream => [stream],
            Stream[] streamArray => streamArray,
            IEnumerable<Stream> streamCollection => streamCollection.ToList(),
            _ => []
        };

        return streams.Count > 0;
    }

    private static void DeleteTemporaryFiles(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private List<TaskItem> GetVisibleTasks()
    {
        return ViewModel.TasksView.Cast<TaskItem>().ToList();
    }

    private static string CreateTasksText(IEnumerable<TaskItem> tasks)
    {
        return string.Join(
            Environment.NewLine,
            tasks.Select(task =>
                $"{task.Id} - {task.Title} [{FormatStatus(task.Status)}] Tags: {task.Tags} POCs: {task.Pocs} Due By: {FormatDate(task.DueBy)} Created: {task.CreatedOn:g} Updated: {task.UpdatedOn:g} Time Spent: {task.TimeSpent}"));
    }

    private static string CreateTasksCsv(IEnumerable<TaskItem> tasks)
    {
        var lines = new List<string>
        {
            "ID,Title,Tags,POCs,Status,DueBy,CreatedOn,UpdatedOn,TimeSpent"
        };

        lines.AddRange(tasks.Select(task => string.Join(
            ",",
            task.Id.ToString(),
            EscapeCsv(task.Title),
            EscapeCsv(task.Tags),
            EscapeCsv(task.Pocs),
            EscapeCsv(FormatStatus(task.Status)),
            EscapeCsv(FormatDate(task.DueBy)),
            EscapeCsv(task.CreatedOn.ToString("g")),
            EscapeCsv(task.UpdatedOn.ToString("g")),
            EscapeCsv(task.TimeSpent.ToString()))));

        return string.Join(Environment.NewLine, lines);
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains('"') && !value.Contains(',') && !value.Contains('\r') && !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static void SetClipboardText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            Clipboard.Clear();
            return;
        }

        Clipboard.SetText(text);
    }

    private static string FormatDate(DateTime? value)
    {
        return value?.ToString("g") ?? string.Empty;
    }

    private static void AddMenuItem(ItemsControl menu, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
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

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private sealed record TaskListColumnOption(TaskListColumn Column, string Header);
}
