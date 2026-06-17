using Microsoft.Data.SqlClient;
using TwoDoThree.Models;
using TaskStatus = TwoDoThree.Models.TaskStatus;

namespace TwoDoThree.Services;

public sealed class SqlServerTaskStore : ITaskStore
{
    private readonly DatabaseSettings settings;
    private string initializedConnectionString = string.Empty;

    public SqlServerTaskStore(DatabaseSettings settings)
    {
        this.settings = settings;
    }

    public bool IsConfigured => settings.IsConfigured;

    public static string TestConnection(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "Enter a SQL Server connection string first.";
        }

        using var connection = new SqlConnection(connectionString);
        connection.Open();
        EnsureSchema(connection);
        return $"Connected to {connection.DataSource}/{connection.Database}. Schema is ready.";
    }

    public IReadOnlyList<TaskItem> LoadTasks()
    {
        if (!IsConfigured)
        {
            return [];
        }

        using var connection = OpenConnection();
        EnsureSchemaIfNeeded(connection);

        var tasks = LoadTaskRows(connection);
        LoadResourceRows(connection, tasks);
        LoadActionRows(connection, tasks);
        LoadActivityRows(connection, tasks);

        return tasks.Values
            .OrderBy(task => task.SortOrder)
            .ThenBy(task => task.Id)
            .ToList();
    }

    public void SaveTask(TaskItem task)
    {
        if (!IsConfigured)
        {
            return;
        }

        using var connection = OpenConnection();
        EnsureSchemaIfNeeded(connection);
        using var transaction = connection.BeginTransaction();

        SaveTaskRow(connection, transaction, task);
        ReplaceResourceRows(connection, transaction, task);
        ReplaceActionRows(connection, transaction, task);
        ReplaceActivityRows(connection, transaction, task);

        transaction.Commit();
    }

    public void DeleteTask(int taskId)
    {
        if (!IsConfigured)
        {
            return;
        }

        using var connection = OpenConnection();
        EnsureSchemaIfNeeded(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM dbo.TwoDoThreeTasks WHERE Id = @Id;";
        AddParameter(command, "@Id", taskId);
        command.ExecuteNonQuery();
    }

    private SqlConnection OpenConnection()
    {
        var connection = new SqlConnection(settings.ConnectionString);
        connection.Open();
        return connection;
    }

    private void EnsureSchemaIfNeeded(SqlConnection connection)
    {
        if (string.Equals(initializedConnectionString, settings.ConnectionString, StringComparison.Ordinal))
        {
            return;
        }

        EnsureSchema(connection);
        initializedConnectionString = settings.ConnectionString;
    }

    private static void EnsureSchema(SqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
IF OBJECT_ID(N'dbo.TwoDoThreeTasks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TwoDoThreeTasks
    (
        Id int NOT NULL CONSTRAINT PK_TwoDoThreeTasks PRIMARY KEY,
        Title nvarchar(500) NOT NULL,
        Tags nvarchar(max) NOT NULL,
        Status nvarchar(32) NOT NULL,
        StatusBeforeActive nvarchar(32) NOT NULL,
        DueBy datetime2 NULL,
        CreatedOn datetime2 NOT NULL,
        UpdatedOn datetime2 NOT NULL,
        TimeSpentSeconds bigint NOT NULL,
        SortOrder int NOT NULL CONSTRAINT DF_TwoDoThreeTasks_SortOrder DEFAULT(0),
        SurfScopeId nvarchar(128) NOT NULL CONSTRAINT DF_TwoDoThreeTasks_SurfScopeId DEFAULT(N''),
        SurfScopeName nvarchar(500) NOT NULL CONSTRAINT DF_TwoDoThreeTasks_SurfScopeName DEFAULT(N'')
    );
END;

IF COL_LENGTH(N'dbo.TwoDoThreeTasks', N'SortOrder') IS NULL
BEGIN
    ALTER TABLE dbo.TwoDoThreeTasks ADD SortOrder int NOT NULL CONSTRAINT DF_TwoDoThreeTasks_SortOrder DEFAULT(0);
END;

IF COL_LENGTH(N'dbo.TwoDoThreeTasks', N'SurfScopeId') IS NULL
BEGIN
    ALTER TABLE dbo.TwoDoThreeTasks ADD SurfScopeId nvarchar(128) NOT NULL CONSTRAINT DF_TwoDoThreeTasks_SurfScopeId DEFAULT(N'');
END;

IF COL_LENGTH(N'dbo.TwoDoThreeTasks', N'SurfScopeName') IS NULL
BEGIN
    ALTER TABLE dbo.TwoDoThreeTasks ADD SurfScopeName nvarchar(500) NOT NULL CONSTRAINT DF_TwoDoThreeTasks_SurfScopeName DEFAULT(N'');
END;

IF OBJECT_ID(N'dbo.TwoDoThreeResources', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TwoDoThreeResources
    (
        ResourceId uniqueidentifier NOT NULL CONSTRAINT PK_TwoDoThreeResources PRIMARY KEY,
        TaskId int NOT NULL,
        SortOrder int NOT NULL,
        Name nvarchar(500) NOT NULL,
        Kind nvarchar(64) NOT NULL,
        Content nvarchar(max) NOT NULL,
        FormattedContent nvarchar(max) NOT NULL,
        CodeLanguage nvarchar(64) NOT NULL,
        EmailMessageId nvarchar(512) NOT NULL,
        EmailFrom nvarchar(500) NOT NULL,
        EmailSubject nvarchar(1000) NOT NULL,
        EmailReceivedOn datetime2 NULL,
        CONSTRAINT FK_TwoDoThreeResources_Tasks FOREIGN KEY (TaskId)
            REFERENCES dbo.TwoDoThreeTasks(Id) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'dbo.TwoDoThreeActions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TwoDoThreeActions
    (
        ActionId uniqueidentifier NOT NULL CONSTRAINT PK_TwoDoThreeActions PRIMARY KEY,
        TaskId int NOT NULL,
        SortOrder int NOT NULL,
        ActionNumber nvarchar(64) NOT NULL,
        ActionText nvarchar(max) NOT NULL,
        IndentLevel int NOT NULL,
        Status nvarchar(32) NOT NULL,
        CONSTRAINT FK_TwoDoThreeActions_Tasks FOREIGN KEY (TaskId)
            REFERENCES dbo.TwoDoThreeTasks(Id) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'dbo.TwoDoThreeActivities', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TwoDoThreeActivities
    (
        ActivityId uniqueidentifier NOT NULL CONSTRAINT PK_TwoDoThreeActivities PRIMARY KEY,
        TaskId int NOT NULL,
        SortOrder int NOT NULL,
        OccurredOn datetime2 NOT NULL,
        Activity nvarchar(max) NOT NULL,
        FromStatus nvarchar(32) NULL,
        ToStatus nvarchar(32) NULL,
        StatusMessage nvarchar(max) NOT NULL,
        CONSTRAINT FK_TwoDoThreeActivities_Tasks FOREIGN KEY (TaskId)
            REFERENCES dbo.TwoDoThreeTasks(Id) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_TwoDoThreeResources_TaskId' AND object_id = OBJECT_ID(N'dbo.TwoDoThreeResources'))
    CREATE INDEX IX_TwoDoThreeResources_TaskId ON dbo.TwoDoThreeResources(TaskId, SortOrder);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_TwoDoThreeActions_TaskId' AND object_id = OBJECT_ID(N'dbo.TwoDoThreeActions'))
    CREATE INDEX IX_TwoDoThreeActions_TaskId ON dbo.TwoDoThreeActions(TaskId, SortOrder);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_TwoDoThreeActivities_TaskId' AND object_id = OBJECT_ID(N'dbo.TwoDoThreeActivities'))
    CREATE INDEX IX_TwoDoThreeActivities_TaskId ON dbo.TwoDoThreeActivities(TaskId, OccurredOn, SortOrder);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_TwoDoThreeTasks_SortOrder' AND object_id = OBJECT_ID(N'dbo.TwoDoThreeTasks'))
    CREATE INDEX IX_TwoDoThreeTasks_SortOrder ON dbo.TwoDoThreeTasks(SortOrder, Id);
""";
        command.ExecuteNonQuery();
    }

    private static Dictionary<int, TaskItem> LoadTaskRows(SqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT Id, Title, Tags, Status, StatusBeforeActive, DueBy, CreatedOn, UpdatedOn, TimeSpentSeconds, SortOrder, SurfScopeId, SurfScopeName
FROM dbo.TwoDoThreeTasks
ORDER BY SortOrder, Id;
""";

        using var reader = command.ExecuteReader();
        var tasks = new Dictionary<int, TaskItem>();
        while (reader.Read())
        {
            var status = ParseEnum(ReadString(reader, "Status"), TaskStatus.Inactive);
            var statusBeforeActive = ParseEnum(ReadString(reader, "StatusBeforeActive"), TaskStatus.Inactive);
            var task = new TaskItem
            {
                Id = ReadInt(reader, "Id"),
                Title = ReadString(reader, "Title"),
                Tags = ReadString(reader, "Tags"),
                DueBy = ReadNullableDateTime(reader, "DueBy"),
                SurfScopeId = ReadString(reader, "SurfScopeId"),
                SurfScopeName = ReadString(reader, "SurfScopeName"),
                CreatedOn = ReadDateTime(reader, "CreatedOn"),
                UpdatedOn = ReadDateTime(reader, "UpdatedOn"),
                TimeSpent = TimeSpan.FromSeconds(ReadLong(reader, "TimeSpentSeconds")),
                SortOrder = ReadInt(reader, "SortOrder")
            };
            task.InitializeStatus(status, statusBeforeActive);
            tasks[task.Id] = task;
        }

        return tasks;
    }

    private static void LoadResourceRows(SqlConnection connection, Dictionary<int, TaskItem> tasks)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT ResourceId, TaskId, Name, Kind, Content, FormattedContent, CodeLanguage,
       EmailMessageId, EmailFrom, EmailSubject, EmailReceivedOn
FROM dbo.TwoDoThreeResources
ORDER BY TaskId, SortOrder;
""";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var taskId = ReadInt(reader, "TaskId");
            if (!tasks.TryGetValue(taskId, out var task))
            {
                continue;
            }

            task.Resources.Add(new ResourceItem
            {
                Id = ReadGuid(reader, "ResourceId"),
                Name = ReadString(reader, "Name"),
                Kind = ParseEnum(ReadString(reader, "Kind"), ResourceKind.Text),
                Content = ReadString(reader, "Content"),
                FormattedContent = ReadString(reader, "FormattedContent"),
                CodeLanguage = ReadString(reader, "CodeLanguage"),
                EmailMessageId = ReadString(reader, "EmailMessageId"),
                EmailFrom = ReadString(reader, "EmailFrom"),
                EmailSubject = ReadString(reader, "EmailSubject"),
                EmailReceivedOn = ReadNullableDateTime(reader, "EmailReceivedOn")
            });
        }
    }

    private static void LoadActionRows(SqlConnection connection, Dictionary<int, TaskItem> tasks)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT ActionId, TaskId, ActionNumber, ActionText, IndentLevel, Status
FROM dbo.TwoDoThreeActions
ORDER BY TaskId, SortOrder;
""";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var taskId = ReadInt(reader, "TaskId");
            if (!tasks.TryGetValue(taskId, out var task))
            {
                continue;
            }

            task.Actions.Add(new ActionItem
            {
                Id = ReadGuid(reader, "ActionId"),
                ActionNumber = ReadString(reader, "ActionNumber"),
                ActionText = ReadString(reader, "ActionText"),
                IndentLevel = ReadInt(reader, "IndentLevel"),
                Status = ParseEnum(ReadString(reader, "Status"), ActionStatus.NotStarted)
            });
        }
    }

    private static void LoadActivityRows(SqlConnection connection, Dictionary<int, TaskItem> tasks)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT ActivityId, TaskId, OccurredOn, Activity, FromStatus, ToStatus, StatusMessage
FROM dbo.TwoDoThreeActivities
ORDER BY TaskId, OccurredOn, SortOrder;
""";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var taskId = ReadInt(reader, "TaskId");
            if (!tasks.TryGetValue(taskId, out var task))
            {
                continue;
            }

            task.Activities.Add(new TaskActivity
            {
                Id = ReadGuid(reader, "ActivityId"),
                OccurredOn = ReadDateTime(reader, "OccurredOn"),
                Activity = ReadString(reader, "Activity"),
                FromStatus = ParseNullableTaskStatus(ReadNullableString(reader, "FromStatus")),
                ToStatus = ParseNullableTaskStatus(ReadNullableString(reader, "ToStatus")),
                StatusMessage = ReadString(reader, "StatusMessage")
            });
        }
    }

    private static void SaveTaskRow(SqlConnection connection, SqlTransaction transaction, TaskItem task)
    {
        using var command = CreateCommand(connection, transaction, """
MERGE dbo.TwoDoThreeTasks WITH (HOLDLOCK) AS target
USING (SELECT @Id AS Id) AS source
ON target.Id = source.Id
WHEN MATCHED THEN
    UPDATE SET
        Title = @Title,
        Tags = @Tags,
        Status = @Status,
        StatusBeforeActive = @StatusBeforeActive,
        DueBy = @DueBy,
        CreatedOn = @CreatedOn,
        UpdatedOn = @UpdatedOn,
        TimeSpentSeconds = @TimeSpentSeconds,
        SortOrder = @SortOrder,
        SurfScopeId = @SurfScopeId,
        SurfScopeName = @SurfScopeName
WHEN NOT MATCHED THEN
    INSERT (Id, Title, Tags, Status, StatusBeforeActive, DueBy, CreatedOn, UpdatedOn, TimeSpentSeconds, SortOrder, SurfScopeId, SurfScopeName)
    VALUES (@Id, @Title, @Tags, @Status, @StatusBeforeActive, @DueBy, @CreatedOn, @UpdatedOn, @TimeSpentSeconds, @SortOrder, @SurfScopeId, @SurfScopeName);
""");

        AddParameter(command, "@Id", task.Id);
        AddParameter(command, "@Title", task.Title);
        AddParameter(command, "@Tags", task.Tags);
        AddParameter(command, "@Status", task.Status.ToString());
        AddParameter(command, "@StatusBeforeActive", task.StatusBeforeActive.ToString());
        AddParameter(command, "@DueBy", task.DueBy);
        AddParameter(command, "@CreatedOn", task.CreatedOn);
        AddParameter(command, "@UpdatedOn", task.UpdatedOn);
        AddParameter(command, "@TimeSpentSeconds", (long)Math.Floor(task.TimeSpent.TotalSeconds));
        AddParameter(command, "@SortOrder", task.SortOrder);
        AddParameter(command, "@SurfScopeId", task.SurfScopeId);
        AddParameter(command, "@SurfScopeName", task.SurfScopeName);
        command.ExecuteNonQuery();
    }

    private static void ReplaceResourceRows(SqlConnection connection, SqlTransaction transaction, TaskItem task)
    {
        ExecuteDeleteChildren(connection, transaction, "dbo.TwoDoThreeResources", task.Id);
        for (var index = 0; index < task.Resources.Count; index++)
        {
            var resource = task.Resources[index];
            using var command = CreateCommand(connection, transaction, """
INSERT INTO dbo.TwoDoThreeResources
    (ResourceId, TaskId, SortOrder, Name, Kind, Content, FormattedContent, CodeLanguage,
     EmailMessageId, EmailFrom, EmailSubject, EmailReceivedOn)
VALUES
    (@ResourceId, @TaskId, @SortOrder, @Name, @Kind, @Content, @FormattedContent, @CodeLanguage,
     @EmailMessageId, @EmailFrom, @EmailSubject, @EmailReceivedOn);
""");
            AddParameter(command, "@ResourceId", resource.Id);
            AddParameter(command, "@TaskId", task.Id);
            AddParameter(command, "@SortOrder", index);
            AddParameter(command, "@Name", resource.Name);
            AddParameter(command, "@Kind", resource.Kind.ToString());
            AddParameter(command, "@Content", resource.Content);
            AddParameter(command, "@FormattedContent", resource.FormattedContent);
            AddParameter(command, "@CodeLanguage", resource.CodeLanguage);
            AddParameter(command, "@EmailMessageId", resource.EmailMessageId);
            AddParameter(command, "@EmailFrom", resource.EmailFrom);
            AddParameter(command, "@EmailSubject", resource.EmailSubject);
            AddParameter(command, "@EmailReceivedOn", resource.EmailReceivedOn);
            command.ExecuteNonQuery();
        }
    }

    private static void ReplaceActionRows(SqlConnection connection, SqlTransaction transaction, TaskItem task)
    {
        ExecuteDeleteChildren(connection, transaction, "dbo.TwoDoThreeActions", task.Id);
        for (var index = 0; index < task.Actions.Count; index++)
        {
            var action = task.Actions[index];
            using var command = CreateCommand(connection, transaction, """
INSERT INTO dbo.TwoDoThreeActions
    (ActionId, TaskId, SortOrder, ActionNumber, ActionText, IndentLevel, Status)
VALUES
    (@ActionId, @TaskId, @SortOrder, @ActionNumber, @ActionText, @IndentLevel, @Status);
""");
            AddParameter(command, "@ActionId", action.Id);
            AddParameter(command, "@TaskId", task.Id);
            AddParameter(command, "@SortOrder", index);
            AddParameter(command, "@ActionNumber", action.ActionNumber);
            AddParameter(command, "@ActionText", action.ActionText);
            AddParameter(command, "@IndentLevel", action.IndentLevel);
            AddParameter(command, "@Status", action.Status.ToString());
            command.ExecuteNonQuery();
        }
    }

    private static void ReplaceActivityRows(SqlConnection connection, SqlTransaction transaction, TaskItem task)
    {
        ExecuteDeleteChildren(connection, transaction, "dbo.TwoDoThreeActivities", task.Id);
        for (var index = 0; index < task.Activities.Count; index++)
        {
            var activity = task.Activities[index];
            using var command = CreateCommand(connection, transaction, """
INSERT INTO dbo.TwoDoThreeActivities
    (ActivityId, TaskId, SortOrder, OccurredOn, Activity, FromStatus, ToStatus, StatusMessage)
VALUES
    (@ActivityId, @TaskId, @SortOrder, @OccurredOn, @Activity, @FromStatus, @ToStatus, @StatusMessage);
""");
            AddParameter(command, "@ActivityId", activity.Id);
            AddParameter(command, "@TaskId", task.Id);
            AddParameter(command, "@SortOrder", index);
            AddParameter(command, "@OccurredOn", activity.OccurredOn);
            AddParameter(command, "@Activity", activity.Activity);
            AddParameter(command, "@FromStatus", activity.FromStatus?.ToString());
            AddParameter(command, "@ToStatus", activity.ToStatus?.ToString());
            AddParameter(command, "@StatusMessage", activity.StatusMessage);
            command.ExecuteNonQuery();
        }
    }

    private static void ExecuteDeleteChildren(SqlConnection connection, SqlTransaction transaction, string tableName, int taskId)
    {
        using var command = CreateCommand(connection, transaction, $"DELETE FROM {tableName} WHERE TaskId = @TaskId;");
        AddParameter(command, "@TaskId", taskId);
        command.ExecuteNonQuery();
    }

    private static SqlCommand CreateCommand(SqlConnection connection, SqlTransaction transaction, string commandText)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        return command;
    }

    private static void AddParameter(SqlCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static TaskStatus? ParseNullableTaskStatus(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : ParseEnum(value, TaskStatus.Inactive);
    }

    private static TEnum ParseEnum<TEnum>(string value, TEnum fallback)
        where TEnum : struct
    {
        return Enum.TryParse<TEnum>(value, out var parsed)
            ? parsed
            : fallback;
    }

    private static Guid ReadGuid(SqlDataReader reader, string name)
    {
        return reader.GetGuid(reader.GetOrdinal(name));
    }

    private static int ReadInt(SqlDataReader reader, string name)
    {
        return reader.GetInt32(reader.GetOrdinal(name));
    }

    private static long ReadLong(SqlDataReader reader, string name)
    {
        return reader.GetInt64(reader.GetOrdinal(name));
    }

    private static string ReadString(SqlDataReader reader, string name)
    {
        return ReadNullableString(reader, name) ?? string.Empty;
    }

    private static string? ReadNullableString(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTime ReadDateTime(SqlDataReader reader, string name)
    {
        return reader.GetDateTime(reader.GetOrdinal(name));
    }

    private static DateTime? ReadNullableDateTime(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }
}
