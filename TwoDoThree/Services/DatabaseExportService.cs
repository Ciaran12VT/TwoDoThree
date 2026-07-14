using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using TwoDoThree.Models;

namespace TwoDoThree.Services;

public sealed class DatabaseExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public DatabaseExportResult Export(
        DatabaseSettings databaseSettings,
        IEmailCacheStore emailCacheStore,
        string zipFilePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(databaseSettings.ConnectionString))
        {
            throw new InvalidOperationException("Enter a SQL Server connection string first.");
        }

        if (string.IsNullOrWhiteSpace(zipFilePath))
        {
            throw new InvalidOperationException("Choose an export file path first.");
        }

        string? directory = Path.GetDirectoryName(zipFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var storeSettings = new DatabaseSettings
        {
            ConnectionString = databaseSettings.ConnectionString
        };

        var taskStore = new SqlServerTaskStore(storeSettings);
        IReadOnlyList<TaskItem> tasks = taskStore.LoadTasks();
        IReadOnlyList<TagResourceCollection> tagResources = taskStore.LoadTagResources();
        IReadOnlyList<EmailMessage> cachedEmails = emailCacheStore.Load();

        using var connection = new SqlConnection(storeSettings.ConnectionString);
        connection.Open();

        var manifest = new DatabaseExportManifest
        {
            ExportedOn = DateTimeOffset.Now,
            SourceServer = connection.DataSource,
            SourceDatabase = connection.Database
        };

        if (File.Exists(zipFilePath))
        {
            File.Delete(zipFilePath);
        }

        var usedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var archive = ZipFile.Open(zipFilePath, ZipArchiveMode.Create))
        {
            DatabaseExportSnapshot databaseSnapshot = CreateDatabaseSnapshot(connection, cancellationToken);
            foreach (DatabaseExportTable table in databaseSnapshot.Tables)
            {
                manifest.TableRowCounts[table.Name] = table.Rows.Count;
            }

            WriteJsonEntry(archive, usedEntryNames, "database/tables.json", databaseSnapshot, cancellationToken);
            WriteJsonEntry(archive, usedEntryNames, "database/tasks.json", tasks, cancellationToken);
            WriteJsonEntry(archive, usedEntryNames, "database/global-tag-resources.json", tagResources, cancellationToken);

            var emailLookup = CreateEmailLookup(cachedEmails);
            foreach (TaskItem task in tasks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string taskPath = CombineEntryPath(
                    "resources",
                    "tasks",
                    $"task-{task.Id}-{task.Title}");
                ExportResources(
                    archive,
                    usedEntryNames,
                    task.Resources,
                    taskPath,
                    emailLookup,
                    manifest,
                    cancellationToken);
            }

            foreach (TagResourceCollection collection in tagResources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string tagPath = CombineEntryPath(
                    "resources",
                    "global-tags",
                    collection.Tag);
                ExportResources(
                    archive,
                    usedEntryNames,
                    collection.Resources,
                    tagPath,
                    emailLookup,
                    manifest,
                    cancellationToken);
            }

            manifest.TaskCount = tasks.Count;
            manifest.GlobalTagResourceSetCount = tagResources.Count;
            manifest.DatabaseTableCount = databaseSnapshot.Tables.Count;
            manifest.DatabaseRowCount = manifest.TableRowCounts.Values.Sum();
            WriteJsonEntry(archive, usedEntryNames, "manifest.json", manifest, cancellationToken);
        }

        return new DatabaseExportResult(
            zipFilePath,
            manifest.TaskCount,
            manifest.DatabaseRowCount,
            manifest.EmailResourceCount,
            manifest.CopiedFileCount,
            manifest.InlineResourceCount,
            manifest.WarningCount);
    }

    private static DatabaseExportSnapshot CreateDatabaseSnapshot(SqlConnection connection, CancellationToken cancellationToken)
    {
        return new DatabaseExportSnapshot
        {
            ExportedOn = DateTimeOffset.Now,
            SourceServer = connection.DataSource,
            SourceDatabase = connection.Database,
            Tables =
            [
                ReadTable(
                    connection,
                    "dbo.TwoDoThreeTasks",
                    """
SELECT Id, Title, Tags, Pocs, Status, StatusBeforeActive, DueBy, CreatedOn, UpdatedOn, TimeSpentSeconds, SortOrder, SurfScopeId, SurfScopeName
FROM dbo.TwoDoThreeTasks
ORDER BY SortOrder, Id;
""",
                    cancellationToken),
                ReadTable(
                    connection,
                    "dbo.TwoDoThreeResources",
                    """
SELECT ResourceId, TaskId, SortOrder, Name, Kind, Content, FormattedContent, CodeLanguage,
       EmailMessageId, EmailFrom, EmailSubject, EmailReceivedOn
FROM dbo.TwoDoThreeResources
ORDER BY TaskId, SortOrder;
""",
                    cancellationToken),
                ReadTable(
                    connection,
                    "dbo.TwoDoThreeTagResources",
                    """
SELECT ResourceId, [Tag], SortOrder, Name, Kind, Content, FormattedContent, CodeLanguage,
       EmailMessageId, EmailFrom, EmailSubject, EmailReceivedOn
FROM dbo.TwoDoThreeTagResources
ORDER BY [Tag], SortOrder;
""",
                    cancellationToken),
                ReadTable(
                    connection,
                    "dbo.TwoDoThreeActions",
                    """
SELECT ActionId, TaskId, SortOrder, ActionNumber, ActionText, IndentLevel, Status
FROM dbo.TwoDoThreeActions
ORDER BY TaskId, SortOrder;
""",
                    cancellationToken),
                ReadTable(
                    connection,
                    "dbo.TwoDoThreeActivities",
                    """
SELECT ActivityId, TaskId, SortOrder, OccurredOn, Activity, FromStatus, ToStatus, StatusMessage
FROM dbo.TwoDoThreeActivities
ORDER BY TaskId, OccurredOn, SortOrder;
""",
                    cancellationToken)
            ]
        };
    }

    private static DatabaseExportTable ReadTable(
        SqlConnection connection,
        string tableName,
        string commandText,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;

        using var reader = command.ExecuteReader();
        var table = new DatabaseExportTable
        {
            Name = tableName
        };

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                row[reader.GetName(index)] = reader.IsDBNull(index)
                    ? null
                    : NormalizeSqlValue(reader.GetValue(index));
            }

            table.Rows.Add(row);
        }

        return table;
    }

    private static object NormalizeSqlValue(object value)
    {
        return value switch
        {
            DateTime dateTime => dateTime,
            DateTimeOffset dateTimeOffset => dateTimeOffset,
            Guid guid => guid,
            TimeSpan timeSpan => timeSpan.ToString(),
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => value
        };
    }

    private static Dictionary<string, EmailMessage> CreateEmailLookup(IEnumerable<EmailMessage> emails)
    {
        return emails
            .Where(email => !string.IsNullOrWhiteSpace(email.Id))
            .GroupBy(email => email.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static void ExportResources(
        ZipArchive archive,
        HashSet<string> usedEntryNames,
        IEnumerable<ResourceItem> resources,
        string ownerPath,
        IReadOnlyDictionary<string, EmailMessage> emailLookup,
        DatabaseExportManifest manifest,
        CancellationToken cancellationToken)
    {
        foreach (ResourceItem resource in resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string resourcePath = CombineEntryPath(
                ownerPath,
                resource.Kind.ToString(),
                $"{resource.Name}-{resource.Id:N}");

            WriteJsonEntry(archive, usedEntryNames, CombineEntryPath(resourcePath, "metadata.json"), ResourceExportSnapshot.FromResource(resource), cancellationToken);

            switch (resource.Kind)
            {
                case ResourceKind.Email:
                    ExportEmailResource(archive, usedEntryNames, resourcePath, resource, emailLookup, manifest, cancellationToken);
                    break;
                case ResourceKind.Sheet:
                    WriteStringEntry(archive, usedEntryNames, CombineEntryPath(resourcePath, $"{resource.Name}.json"), resource.Content, cancellationToken);
                    WriteStringEntry(archive, usedEntryNames, CombineEntryPath(resourcePath, $"{resource.Name}.csv"), SheetResourceSerializer.ToCsv(resource.Content), cancellationToken);
                    manifest.InlineResourceCount++;
                    break;
                case ResourceKind.CodeSnippet:
                    WriteStringEntry(archive, usedEntryNames, CombineEntryPath(resourcePath, $"{resource.Name}{GetCodeExtension(resource.CodeLanguage)}"), resource.Content, cancellationToken);
                    manifest.InlineResourceCount++;
                    break;
                case ResourceKind.Text:
                    WriteStringEntry(archive, usedEntryNames, CombineEntryPath(resourcePath, $"{resource.Name}.txt"), resource.Content, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(resource.FormattedContent))
                    {
                        WriteStringEntry(archive, usedEntryNames, CombineEntryPath(resourcePath, $"{resource.Name}.xaml"), resource.FormattedContent, cancellationToken);
                    }

                    manifest.InlineResourceCount++;
                    break;
                case ResourceKind.Image:
                case ResourceKind.Audio:
                    ExportLocalPathResource(archive, usedEntryNames, resourcePath, resource.Content, manifest, cancellationToken);
                    break;
                case ResourceKind.SurfResource:
                    ExportSurfResource(archive, usedEntryNames, resourcePath, resource, manifest, cancellationToken);
                    break;
            }
        }
    }

    private static void ExportEmailResource(
        ZipArchive archive,
        HashSet<string> usedEntryNames,
        string resourcePath,
        ResourceItem resource,
        IReadOnlyDictionary<string, EmailMessage> emailLookup,
        DatabaseExportManifest manifest,
        CancellationToken cancellationToken)
    {
        EmailMessage? cachedEmail = !string.IsNullOrWhiteSpace(resource.EmailMessageId)
                                    && emailLookup.TryGetValue(resource.EmailMessageId, out EmailMessage? email)
            ? email
            : null;

        string plainText = cachedEmail?.Body ?? resource.Content;
        string html = cachedEmail?.HtmlBody ?? resource.FormattedContent;
        var emailSnapshot = EmailExportSnapshot.FromResource(resource, cachedEmail);

        WriteJsonEntry(archive, usedEntryNames, CombineEntryPath(resourcePath, "email.json"), emailSnapshot, cancellationToken);
        WriteStringEntry(archive, usedEntryNames, CombineEntryPath(resourcePath, $"{resource.Name}.txt"), plainText, cancellationToken);
        if (!string.IsNullOrWhiteSpace(html))
        {
            WriteStringEntry(archive, usedEntryNames, CombineEntryPath(resourcePath, $"{resource.Name}.html"), html, cancellationToken);
        }

        manifest.EmailResourceCount++;
    }

    private static void ExportSurfResource(
        ZipArchive archive,
        HashSet<string> usedEntryNames,
        string resourcePath,
        ResourceItem resource,
        DatabaseExportManifest manifest,
        CancellationToken cancellationToken)
    {
        if (!SurfResourceLink.TryParse(resource.Content, out SurfResourceLink link))
        {
            manifest.Warnings.Add($"Surf resource '{resource.Name}' has invalid link data.");
            return;
        }

        WriteJsonEntry(archive, usedEntryNames, CombineEntryPath(resourcePath, "surf-resource-link.json"), link, cancellationToken);
        if (File.Exists(link.ResourcePath) || Directory.Exists(link.ResourcePath))
        {
            ExportLocalPathResource(archive, usedEntryNames, resourcePath, link.ResourcePath, manifest, cancellationToken);
        }
    }

    private static void ExportLocalPathResource(
        ZipArchive archive,
        HashSet<string> usedEntryNames,
        string resourcePath,
        string path,
        DatabaseExportManifest manifest,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            manifest.Warnings.Add($"Resource path under '{resourcePath}' is empty.");
            return;
        }

        if (File.Exists(path))
        {
            AddFileEntry(archive, usedEntryNames, path, CombineEntryPath(resourcePath, Path.GetFileName(path)), manifest, cancellationToken);
            return;
        }

        if (Directory.Exists(path))
        {
            AddDirectoryEntries(archive, usedEntryNames, path, CombineEntryPath(resourcePath, Path.GetFileName(path)), manifest, cancellationToken);
            return;
        }

        manifest.Warnings.Add($"Linked resource path not found: {path}");
    }

    private static void AddDirectoryEntries(
        ZipArchive archive,
        HashSet<string> usedEntryNames,
        string directoryPath,
        string entryPath,
        DatabaseExportManifest manifest,
        CancellationToken cancellationToken)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            manifest.Warnings.Add($"Could not read linked directory '{directoryPath}': {ex.Message}");
            return;
        }

        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = Path.GetRelativePath(directoryPath, file);
            AddFileEntry(
                archive,
                usedEntryNames,
                file,
                CombineEntryPath(new[] { entryPath }
                    .Concat(relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))),
                manifest,
                cancellationToken);
        }
    }

    private static void AddFileEntry(
        ZipArchive archive,
        HashSet<string> usedEntryNames,
        string sourcePath,
        string preferredEntryName,
        DatabaseExportManifest manifest,
        CancellationToken cancellationToken)
    {
        try
        {
            var fileInfo = new FileInfo(sourcePath);
            string entryName = ReserveEntryName(usedEntryNames, preferredEntryName);
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            entry.LastWriteTime = fileInfo.LastWriteTime;

            using Stream input = File.OpenRead(sourcePath);
            using Stream output = entry.Open();
            input.CopyTo(output);
            cancellationToken.ThrowIfCancellationRequested();

            manifest.CopiedFileCount++;
            manifest.CopiedFiles.Add(new CopiedResourceFile(sourcePath, entryName, fileInfo.Length));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            manifest.Warnings.Add($"Could not copy linked file '{sourcePath}': {ex.Message}");
        }
    }

    private static void WriteJsonEntry<T>(
        ZipArchive archive,
        HashSet<string> usedEntryNames,
        string preferredEntryName,
        T value,
        CancellationToken cancellationToken)
    {
        WriteStringEntry(
            archive,
            usedEntryNames,
            preferredEntryName,
            JsonSerializer.Serialize(value, JsonOptions),
            cancellationToken);
    }

    private static void WriteStringEntry(
        ZipArchive archive,
        HashSet<string> usedEntryNames,
        string preferredEntryName,
        string content,
        CancellationToken cancellationToken)
    {
        string entryName = ReserveEntryName(usedEntryNames, preferredEntryName);
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content ?? string.Empty);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static string ReserveEntryName(HashSet<string> usedEntryNames, string preferredEntryName)
    {
        string normalizedName = NormalizeEntryName(preferredEntryName);
        if (usedEntryNames.Add(normalizedName))
        {
            return normalizedName;
        }

        string directory = Path.GetDirectoryName(normalizedName)?.Replace('\\', '/') ?? string.Empty;
        string fileName = Path.GetFileNameWithoutExtension(normalizedName);
        string extension = Path.GetExtension(normalizedName);
        for (var suffix = 2; suffix < int.MaxValue; suffix++)
        {
            string candidateFileName = $"{fileName} ({suffix}){extension}";
            string candidate = string.IsNullOrWhiteSpace(directory)
                ? candidateFileName
                : $"{directory}/{candidateFileName}";
            if (usedEntryNames.Add(candidate))
            {
                return candidate;
            }
        }

        string fallback = $"{normalizedName}-{Guid.NewGuid():N}";
        usedEntryNames.Add(fallback);
        return fallback;
    }

    private static string CombineEntryPath(params string[] segments)
    {
        return CombineEntryPath(segments.AsEnumerable());
    }

    private static string CombineEntryPath(IEnumerable<string> segments)
    {
        return string.Join(
            "/",
            segments
                .SelectMany(segment => segment.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(GetSafePathSegment)
                .Where(segment => !string.IsNullOrWhiteSpace(segment)));
    }

    private static string NormalizeEntryName(string entryName)
    {
        string normalized = CombineEntryPath(entryName);
        return string.IsNullOrWhiteSpace(normalized)
            ? $"entry-{Guid.NewGuid():N}"
            : normalized;
    }

    private static string GetSafePathSegment(string value)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? "unnamed" : value.Trim();
        var invalidChars = Path.GetInvalidFileNameChars()
            .Concat([':', '*', '?', '"', '<', '>', '|'])
            .ToHashSet();
        var builder = new StringBuilder(trimmed.Length);

        foreach (char current in trimmed)
        {
            builder.Append(invalidChars.Contains(current) || char.IsControl(current) ? '_' : current);
        }

        string safe = builder.ToString().Trim('.', ' ');
        return string.IsNullOrWhiteSpace(safe) ? "unnamed" : safe;
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

    private sealed class DatabaseExportSnapshot
    {
        public DateTimeOffset ExportedOn { get; set; }

        public string SourceServer { get; set; } = string.Empty;

        public string SourceDatabase { get; set; } = string.Empty;

        public List<DatabaseExportTable> Tables { get; set; } = [];
    }

    private sealed class DatabaseExportTable
    {
        public string Name { get; set; } = string.Empty;

        public List<Dictionary<string, object?>> Rows { get; set; } = [];
    }

    private sealed class DatabaseExportManifest
    {
        public DateTimeOffset ExportedOn { get; set; }

        public string SourceServer { get; set; } = string.Empty;

        public string SourceDatabase { get; set; } = string.Empty;

        public int DatabaseTableCount { get; set; }

        public int DatabaseRowCount { get; set; }

        public int TaskCount { get; set; }

        public int GlobalTagResourceSetCount { get; set; }

        public int EmailResourceCount { get; set; }

        public int InlineResourceCount { get; set; }

        public int CopiedFileCount { get; set; }

        public Dictionary<string, int> TableRowCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<CopiedResourceFile> CopiedFiles { get; } = [];

        public List<string> Warnings { get; } = [];

        public int WarningCount => Warnings.Count;
    }

    private sealed record CopiedResourceFile(string SourcePath, string EntryName, long SizeBytes);

    private sealed class ResourceExportSnapshot
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public ResourceKind Kind { get; set; }

        public string Content { get; set; } = string.Empty;

        public string FormattedContent { get; set; } = string.Empty;

        public string CodeLanguage { get; set; } = string.Empty;

        public string EmailMessageId { get; set; } = string.Empty;

        public string EmailFrom { get; set; } = string.Empty;

        public string EmailSubject { get; set; } = string.Empty;

        public DateTime? EmailReceivedOn { get; set; }

        public static ResourceExportSnapshot FromResource(ResourceItem resource)
        {
            return new ResourceExportSnapshot
            {
                Id = resource.Id,
                Name = resource.Name,
                Kind = resource.Kind,
                Content = resource.Content,
                FormattedContent = resource.FormattedContent,
                CodeLanguage = resource.CodeLanguage,
                EmailMessageId = resource.EmailMessageId,
                EmailFrom = resource.EmailFrom,
                EmailSubject = resource.EmailSubject,
                EmailReceivedOn = resource.EmailReceivedOn
            };
        }
    }

    private sealed class EmailExportSnapshot
    {
        public string Id { get; set; } = string.Empty;

        public string From { get; set; } = string.Empty;

        public string To { get; set; } = string.Empty;

        public string Cc { get; set; } = string.Empty;

        public string Subject { get; set; } = string.Empty;

        public DateTime ReceivedOn { get; set; }

        public string Preview { get; set; } = string.Empty;

        public bool WasFoundInCache { get; set; }

        public static EmailExportSnapshot FromResource(ResourceItem resource, EmailMessage? cachedEmail)
        {
            return cachedEmail is null
                ? new EmailExportSnapshot
                {
                    Id = resource.EmailMessageId,
                    From = resource.EmailFrom,
                    Subject = resource.EmailSubject,
                    ReceivedOn = resource.EmailReceivedOn ?? DateTime.MinValue,
                    WasFoundInCache = false
                }
                : new EmailExportSnapshot
                {
                    Id = cachedEmail.Id,
                    From = cachedEmail.From,
                    To = cachedEmail.To,
                    Cc = cachedEmail.Cc,
                    Subject = cachedEmail.Subject,
                    ReceivedOn = cachedEmail.ReceivedOn,
                    Preview = cachedEmail.Preview,
                    WasFoundInCache = true
                };
        }
    }
}

public sealed record DatabaseExportResult(
    string ZipFilePath,
    int TaskCount,
    int DatabaseRowCount,
    int EmailResourceCount,
    int CopiedFileCount,
    int InlineResourceCount,
    int WarningCount);
