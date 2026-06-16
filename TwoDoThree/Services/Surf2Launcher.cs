using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using TwoDoThree.Models;

namespace TwoDoThree.Services;

public sealed class Surf2Launcher : ISurf2Launcher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public Task OpenResourceAsync(
        Surf2IntegrationSettings settings,
        SurfResourceLink resourceLink,
        CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured)
        {
            throw new InvalidOperationException("Enable the Surf2 integration and enter its database connection string first.");
        }

        if (string.IsNullOrWhiteSpace(resourceLink.ScopeId) &&
            string.IsNullOrWhiteSpace(resourceLink.ScopeName))
        {
            throw new InvalidOperationException("The Surf resource is missing its Surf2 scope.");
        }

        if (string.IsNullOrWhiteSpace(resourceLink.ResourcePath))
        {
            throw new InvalidOperationException("The Surf resource is missing its Surf2 path.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var request = new Surf2ExternalOpenRequest
        {
            ConnectionString = settings.ConnectionString,
            ScopeId = resourceLink.ScopeId,
            ScopeName = resourceLink.ScopeName,
            ResourcePath = resourceLink.ResourcePath,
            ResourceName = resourceLink.ResourceName,
            ResourceKind = resourceLink.ResourceKind,
            LineNumber = resourceLink.LineNumber,
            ColumnNumber = resourceLink.ColumnNumber
        };
        string json = JsonSerializer.Serialize(request, JsonOptions);
        string encodedJson = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        string executablePath = ResolveExecutablePath(settings);

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false
        };
        string? workingDirectory = Path.GetDirectoryName(executablePath);
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        startInfo.ArgumentList.Add("--external-open");
        startInfo.ArgumentList.Add("--surf2-open-json");
        startInfo.ArgumentList.Add(encodedJson);

        Process.Start(startInfo);
        return Task.CompletedTask;
    }

    private static string ResolveExecutablePath(Surf2IntegrationSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ExecutablePath) &&
            File.Exists(settings.ExecutablePath))
        {
            return settings.ExecutablePath;
        }

        string? environmentPath = Environment.GetEnvironmentVariable("SURF2_EXE");
        if (!string.IsNullOrWhiteSpace(environmentPath) && File.Exists(environmentPath))
        {
            return environmentPath;
        }

        foreach (string candidate in GetLocalDevelopmentCandidates())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "Surf2.exe";
    }

    private static IEnumerable<string> GetLocalDevelopmentCandidates()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            yield return Path.Combine(
                current.FullName,
                "Surf 2.0",
                "Surf2",
                "bin",
                "Debug",
                "net10.0-windows",
                "Surf2.exe");
            yield return Path.Combine(
                current.FullName,
                "Surf 2.0",
                "Surf2",
                "bin",
                "Release",
                "net10.0-windows",
                "Surf2.exe");

            if (current.Parent != null)
            {
                yield return Path.Combine(
                    current.Parent.FullName,
                    "Surf 2.0",
                    "Surf2",
                    "bin",
                    "Debug",
                    "net10.0-windows",
                    "Surf2.exe");
                yield return Path.Combine(
                    current.Parent.FullName,
                    "Surf 2.0",
                    "Surf2",
                    "bin",
                    "Release",
                    "net10.0-windows",
                    "Surf2.exe");
            }

            current = current.Parent;
        }
    }

    private sealed class Surf2ExternalOpenRequest
    {
        public string ConnectionString { get; set; } = string.Empty;

        public string ScopeId { get; set; } = string.Empty;

        public string ScopeName { get; set; } = string.Empty;

        public string ResourcePath { get; set; } = string.Empty;

        public string ResourceName { get; set; } = string.Empty;

        public string ResourceKind { get; set; } = string.Empty;

        public int? LineNumber { get; set; }

        public int? ColumnNumber { get; set; }
    }
}
