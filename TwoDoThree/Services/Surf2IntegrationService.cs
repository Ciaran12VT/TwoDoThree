using System.IO;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using TwoDoThree.Models;

namespace TwoDoThree.Services;

public sealed class Surf2IntegrationService : ISurf2IntegrationService
{
    private const string ScopeLibraryDocumentKey = "scope-library";
    private const string DatabaseSnapshotsDocumentKey = "database-snapshots";
    private const string DiagramLibraryDocumentKey = "diagram-library";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<string> TestConnectionAsync(
        Surf2IntegrationSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            return "Enter the Surf2 database connection string first.";
        }

        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
IF OBJECT_ID(N'app.Surf2Documents', N'U') IS NULL
BEGIN
    SELECT CAST(-1 AS int);
    RETURN;
END;

SELECT COUNT(*)
FROM app.Surf2Documents
WHERE DocumentKey = N'scope-library';
""";

        int scopeLibraryCount = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return scopeLibraryCount < 0
            ? $"Connected to {connection.DataSource}/{connection.Database}, but this does not look like a Surf2 database."
            : $"Connected to {connection.DataSource}/{connection.Database}. Found the Surf2 scope library.";
    }

    public async Task<IReadOnlyList<Surf2ScopeOption>> LoadScopesAsync(
        Surf2IntegrationSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured)
        {
            return [];
        }

        ScopeLibraryDocument? scopeLibrary = await LoadDocumentAsync<ScopeLibraryDocument>(
            settings.ConnectionString,
            ScopeLibraryDocumentKey,
            cancellationToken);

        return scopeLibrary?.Scopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope.ScopeId))
            .OrderBy(scope => scope.Name)
            .Select(scope => new Surf2ScopeOption
            {
                ScopeId = scope.ScopeId,
                Name = scope.Name,
                Description = scope.Description
            })
            .ToList() ?? [];
    }

    public async Task<IReadOnlyList<Surf2ResourceCandidate>> LoadResourcesAsync(
        Surf2IntegrationSettings settings,
        string scopeId,
        CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured || string.IsNullOrWhiteSpace(scopeId))
        {
            return [];
        }

        ScopeLibraryDocument? scopeLibrary = await LoadDocumentAsync<ScopeLibraryDocument>(
            settings.ConnectionString,
            ScopeLibraryDocumentKey,
            cancellationToken);
        ScopeDocument? scope = scopeLibrary?.Scopes.FirstOrDefault(candidate =>
            string.Equals(candidate.ScopeId, scopeId, StringComparison.OrdinalIgnoreCase));
        if (scope == null)
        {
            return [];
        }

        DatabaseSnapshotLibraryDocument databaseSnapshots =
            await LoadDocumentAsync<DatabaseSnapshotLibraryDocument>(
                settings.ConnectionString,
                DatabaseSnapshotsDocumentKey,
                cancellationToken) ?? new DatabaseSnapshotLibraryDocument();
        DiagramLibraryDocument diagramLibrary =
            await LoadDocumentAsync<DiagramLibraryDocument>(
                settings.ConnectionString,
                DiagramLibraryDocumentKey,
                cancellationToken) ?? new DiagramLibraryDocument();

        var resources = new List<Surf2ResourceCandidate>();
        var addedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ScopedResourceDocument scopedResource in scope.Resources)
        {
            switch (scopedResource.Kind)
            {
                case "Folder":
                    AddFolderResource(scope, scopedResource, resources, addedKeys);
                    break;
                case "File":
                    AddFileResource(scope, scopedResource, resources, addedKeys);
                    break;
                case "DatabaseSnapshot":
                    AddDatabaseResource(scope, scopedResource, databaseSnapshots, resources, addedKeys);
                    break;
                case "Diagram":
                    AddDiagramResource(scope, scopedResource, diagramLibrary, resources, addedKeys);
                    break;
            }
        }

        return resources
            .OrderBy(resource => resource.Type)
            .ThenBy(resource => resource.Name)
            .ThenBy(resource => resource.Path)
            .ToList();
    }

    private static async Task<T?> LoadDocumentAsync<T>(
        string connectionString,
        string documentKey,
        CancellationToken cancellationToken)
        where T : class
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
IF OBJECT_ID(N'app.Surf2Documents', N'U') IS NULL
BEGIN
    SELECT CAST(NULL AS nvarchar(max)) AS PayloadJson;
    RETURN;
END;

SELECT PayloadJson
FROM app.Surf2Documents
WHERE DocumentKey = @DocumentKey;
""";
        command.Parameters.Add(new SqlParameter("@DocumentKey", documentKey));

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is not string payload || string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        return JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }

    private static void AddFolderResource(
        ScopeDocument scope,
        ScopedResourceDocument scopedResource,
        List<Surf2ResourceCandidate> resources,
        HashSet<string> addedKeys)
    {
        if (!Directory.Exists(scopedResource.Path))
        {
            return;
        }

        AddFolder(scope, scopedResource.Path, GetScopedResourceDisplayName(scopedResource), resources, addedKeys);
        if (!scopedResource.IncludeChildren)
        {
            return;
        }

        AddFolderContents(scope, scopedResource.Path, resources, addedKeys);
    }

    private static void AddFolderContents(
        ScopeDocument scope,
        string folderPath,
        List<Surf2ResourceCandidate> resources,
        HashSet<string> addedKeys)
    {
        foreach (string directoryPath in EnumerateDirectoriesSafely(folderPath).OrderBy(Path.GetFileName))
        {
            AddFolder(scope, directoryPath, GetFileSystemDisplayName(directoryPath), resources, addedKeys);
            AddFolderContents(scope, directoryPath, resources, addedKeys);
        }

        foreach (string filePath in EnumerateFilesSafely(folderPath).OrderBy(Path.GetFileName))
        {
            AddFile(scope, filePath, GetFileSystemDisplayName(filePath), "File", resources, addedKeys);
        }
    }

    private static void AddFileResource(
        ScopeDocument scope,
        ScopedResourceDocument scopedResource,
        List<Surf2ResourceCandidate> resources,
        HashSet<string> addedKeys)
    {
        if (File.Exists(scopedResource.Path))
        {
            AddFile(scope, scopedResource.Path, GetScopedResourceDisplayName(scopedResource), "File", resources, addedKeys);
        }
    }

    private static void AddDatabaseResource(
        ScopeDocument scope,
        ScopedResourceDocument scopedResource,
        DatabaseSnapshotLibraryDocument databaseSnapshots,
        List<Surf2ResourceCandidate> resources,
        HashSet<string> addedKeys)
    {
        DatabaseMetadataSnapshotDocument? snapshot = databaseSnapshots.Snapshots.FirstOrDefault(candidate =>
            string.Equals(candidate.SnapshotId, scopedResource.Path, StringComparison.OrdinalIgnoreCase));
        if (snapshot == null)
        {
            return;
        }

        string displayName = !string.IsNullOrWhiteSpace(scopedResource.DisplayNameOverride)
            ? scopedResource.DisplayNameOverride
            : GetSnapshotDocumentName(snapshot);
        AddResource(scope, resources, addedKeys, displayName, "Database", CreateSnapshotDocumentPath(snapshot), "Database");

        foreach (SqlDatabaseObjectDocument databaseObject in snapshot.Objects
                     .OrderBy(item => item.Kind)
                     .ThenBy(item => item.SchemaName)
                     .ThenBy(item => item.ObjectName))
        {
            string fullName = FormatPlainMultipartName(databaseObject.SchemaName, databaseObject.ObjectName);
            AddResource(
                scope,
                resources,
                addedKeys,
                fullName,
                GetSqlObjectTypeDisplay(databaseObject.Kind),
                CreateObjectDocumentPath(snapshot, databaseObject),
                databaseObject.Kind);
        }

        foreach (SqlTableDocument table in snapshot.Tables
                     .OrderBy(item => item.SchemaName)
                     .ThenBy(item => item.TableName))
        {
            string fullName = FormatPlainMultipartName(table.SchemaName, table.TableName);
            AddResource(
                scope,
                resources,
                addedKeys,
                fullName,
                "Table",
                CreateTableDocumentPath(snapshot, table),
                "Table");
        }
    }

    private static void AddDiagramResource(
        ScopeDocument scope,
        ScopedResourceDocument scopedResource,
        DiagramLibraryDocument diagramLibrary,
        List<Surf2ResourceCandidate> resources,
        HashSet<string> addedKeys)
    {
        DiagramDocument? diagram = diagramLibrary.Diagrams.FirstOrDefault(candidate =>
            string.Equals(candidate.DiagramId, scopedResource.Path, StringComparison.OrdinalIgnoreCase));
        if (diagram == null)
        {
            return;
        }

        string displayName = !string.IsNullOrWhiteSpace(scopedResource.DisplayNameOverride)
            ? scopedResource.DisplayNameOverride
            : diagram.Name;
        AddResource(
            scope,
            resources,
            addedKeys,
            displayName,
            "Diagram",
            $"surf2://diagram/{scopedResource.Path}",
            "Diagram");
    }

    private static void AddFolder(
        ScopeDocument scope,
        string folderPath,
        string displayName,
        List<Surf2ResourceCandidate> resources,
        HashSet<string> addedKeys)
    {
        AddResource(scope, resources, addedKeys, displayName, "Folder", folderPath, "Folder");
    }

    private static void AddFile(
        ScopeDocument scope,
        string filePath,
        string displayName,
        string fallbackType,
        List<Surf2ResourceCandidate> resources,
        HashSet<string> addedKeys)
    {
        string extension = Path.GetExtension(filePath);
        string type = string.IsNullOrWhiteSpace(extension)
            ? fallbackType
            : extension.TrimStart('.').ToUpperInvariant();
        AddResource(scope, resources, addedKeys, displayName, type, filePath, "File");
    }

    private static void AddResource(
        ScopeDocument scope,
        List<Surf2ResourceCandidate> resources,
        HashSet<string> addedKeys,
        string name,
        string type,
        string path,
        string kind)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string key = $"{kind}|{path}";
        if (!addedKeys.Add(key))
        {
            return;
        }

        resources.Add(new Surf2ResourceCandidate
        {
            ScopeId = scope.ScopeId,
            ScopeName = scope.Name,
            Name = string.IsNullOrWhiteSpace(name) ? path : name,
            Type = type,
            Path = path,
            Kind = kind
        });
    }

    private static string GetScopedResourceDisplayName(ScopedResourceDocument resource)
    {
        if (!string.IsNullOrWhiteSpace(resource.DisplayNameOverride))
        {
            return resource.DisplayNameOverride;
        }

        if (string.Equals(resource.Kind, "DatabaseSnapshot", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(resource.Kind, "Diagram", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(resource.Path) ? resource.Kind : resource.Path;
        }

        string name = Path.GetFileName(resource.Path);
        return string.IsNullOrWhiteSpace(name) ? resource.Path : name;
    }

    private static string GetFileSystemDisplayName(string path)
    {
        string name = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private static IEnumerable<string> EnumerateDirectoriesSafely(string folderPath)
    {
        try
        {
            return Directory.EnumerateDirectories(folderPath).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            return [];
        }
    }

    private static IEnumerable<string> EnumerateFilesSafely(string folderPath)
    {
        try
        {
            return Directory.EnumerateFiles(folderPath).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            return [];
        }
    }

    private static string CreateSnapshotDocumentPath(DatabaseMetadataSnapshotDocument snapshot)
    {
        return $"db://{EscapeSnapshotDocumentSegment(GetSnapshotDocumentName(snapshot))}";
    }

    private static string CreateObjectDocumentPath(
        DatabaseMetadataSnapshotDocument snapshot,
        SqlDatabaseObjectDocument databaseObject)
    {
        string fullName = FormatPlainMultipartName(databaseObject.SchemaName, databaseObject.ObjectName);
        return $"db://{EscapeSnapshotDocumentSegment(GetSnapshotDocumentName(snapshot))}/object/{databaseObject.Kind}/{Uri.EscapeDataString(fullName)}.sql";
    }

    private static string CreateTableDocumentPath(DatabaseMetadataSnapshotDocument snapshot, SqlTableDocument table)
    {
        string fullName = FormatPlainMultipartName(table.SchemaName, table.TableName);
        return $"db://{EscapeSnapshotDocumentSegment(GetSnapshotDocumentName(snapshot))}/table/{Uri.EscapeDataString(fullName)}.sql";
    }

    private static string GetSnapshotDocumentName(DatabaseMetadataSnapshotDocument snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.DisplayName) &&
            !string.Equals(snapshot.DisplayName, "Database Snapshot", StringComparison.OrdinalIgnoreCase))
        {
            return snapshot.DisplayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(snapshot.DatabaseName))
        {
            return snapshot.DatabaseName.Trim();
        }

        return string.IsNullOrWhiteSpace(snapshot.DisplayName)
            ? snapshot.SnapshotId
            : snapshot.DisplayName.Trim();
    }

    private static string EscapeSnapshotDocumentSegment(string value)
    {
        return value
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace("/", "%2F", StringComparison.Ordinal)
            .Replace("\\", "%5C", StringComparison.Ordinal)
            .Replace("#", "%23", StringComparison.Ordinal)
            .Replace("?", "%3F", StringComparison.Ordinal);
    }

    private static string FormatPlainMultipartName(string schemaName, string objectName)
    {
        string schema = string.IsNullOrWhiteSpace(schemaName) ? "dbo" : schemaName;
        return $"{schema}.{objectName}";
    }

    private static string GetSqlObjectTypeDisplay(string kind)
    {
        return kind switch
        {
            "StoredProcedure" => "Stored Procedure",
            "View" => "View",
            "Function" => "Function",
            "Trigger" => "Trigger",
            _ => "Database Object"
        };
    }

    private sealed class ScopeLibraryDocument
    {
        public List<ScopeDocument> Scopes { get; set; } = [];
    }

    private sealed class ScopeDocument
    {
        public string ScopeId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public List<ScopedResourceDocument> Resources { get; set; } = [];
    }

    private sealed class ScopedResourceDocument
    {
        public string Kind { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public string DisplayNameOverride { get; set; } = string.Empty;

        public bool IncludeChildren { get; set; } = true;
    }

    private sealed class DatabaseSnapshotLibraryDocument
    {
        public List<DatabaseMetadataSnapshotDocument> Snapshots { get; set; } = [];
    }

    private sealed class DatabaseMetadataSnapshotDocument
    {
        public string SnapshotId { get; set; } = string.Empty;

        public string DisplayName { get; set; } = "Database Snapshot";

        public string DatabaseName { get; set; } = string.Empty;

        public List<SqlDatabaseObjectDocument> Objects { get; set; } = [];

        public List<SqlTableDocument> Tables { get; set; } = [];
    }

    private sealed class SqlDatabaseObjectDocument
    {
        public string SchemaName { get; set; } = "dbo";

        public string ObjectName { get; set; } = string.Empty;

        public string Kind { get; set; } = string.Empty;
    }

    private sealed class SqlTableDocument
    {
        public string SchemaName { get; set; } = "dbo";

        public string TableName { get; set; } = string.Empty;
    }

    private sealed class DiagramLibraryDocument
    {
        public List<DiagramDocument> Diagrams { get; set; } = [];
    }

    private sealed class DiagramDocument
    {
        public string DiagramId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
    }
}
