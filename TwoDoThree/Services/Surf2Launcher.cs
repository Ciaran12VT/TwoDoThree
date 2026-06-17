using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using TwoDoThree.Models;

namespace TwoDoThree.Services;

public sealed class Surf2Launcher : ISurf2Launcher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public async Task OpenResourceAsync(
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

        if (await TrySendToExistingSurf2Async(request, TimeSpan.FromMilliseconds(900), cancellationToken))
        {
            return;
        }

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

        Process? process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Windows did not start the Surf2 process.");
        }

        await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
        if (process.HasExited && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Surf2 exited before opening the resource. Exit code: {process.ExitCode}.");
        }
    }

    private static async Task<bool> TrySendToExistingSurf2Async(
        Surf2ExternalOpenRequest request,
        TimeSpan connectTimeout,
        CancellationToken cancellationToken)
    {
        string pipeName = CreateSurf2PipeName(request.ConnectionString);

        await using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            pipe.Connect(ToTimeoutMilliseconds(connectTimeout));
            cancellationToken.ThrowIfCancellationRequested();

            await using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true)
            {
                AutoFlush = true
            };

            string requestJson = JsonSerializer.Serialize(request, JsonOptions);
            await writer.WriteLineAsync(requestJson.AsMemory(), cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static int ToTimeoutMilliseconds(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return 0;
        }

        return timeout.TotalMilliseconds >= int.MaxValue
            ? int.MaxValue
            : Math.Max(1, (int)Math.Ceiling(timeout.TotalMilliseconds));
    }

    private static string CreateSurf2PipeName(string connectionString)
    {
        string normalizedConnectionString;
        try
        {
            normalizedConnectionString = NormalizeSurf2ConnectionString(connectionString).ToUpperInvariant();
        }
        catch (ArgumentException)
        {
            normalizedConnectionString = connectionString.Trim().ToUpperInvariant();
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedConnectionString));
        return $"Surf2.ExternalOpen.{Convert.ToHexString(hash)[..16]}";
    }

    private static string NormalizeSurf2ConnectionString(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);

        if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
        {
            builder.InitialCatalog = "Surf2";
        }

        if (!ContainsKeyword(connectionString, "TrustServerCertificate") &&
            !ContainsKeyword(connectionString, "Trust Server Certificate"))
        {
            builder.TrustServerCertificate = true;
        }

        if (!ContainsKeyword(connectionString, "Connect Timeout") &&
            !ContainsKeyword(connectionString, "Connection Timeout"))
        {
            builder.ConnectTimeout = 10;
        }

        return builder.ConnectionString;
    }

    private static bool ContainsKeyword(string connectionString, string keyword)
    {
        return connectionString.Contains(keyword, StringComparison.OrdinalIgnoreCase);
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
