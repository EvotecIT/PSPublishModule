using System.Text.Json;

namespace PowerForge;

public sealed partial class AppleNotarizationService
{
    private static AppleReleaseSourceMutationMonitor CreateArtifactMutationMonitor(
        string artifactPath,
        string scopeDescription,
        string readerDescription,
        string failureInstruction,
        bool enableImmediately = true)
    {
        var fullPath = Path.GetFullPath(artifactPath);
        var isDirectory = Directory.Exists(fullPath);
        return new AppleReleaseSourceMutationMonitor(
            Path.GetDirectoryName(fullPath)!,
            scopeDescription,
            readerDescription,
            failureInstruction,
            enableImmediately,
            exactPath: fullPath,
            includeExactPathDescendants: isDirectory);
    }

    private static (string? Id, string? Status) ParseSubmission(ProcessRunResult result)
    {
        var payload = string.IsNullOrWhiteSpace(result.StdOut) ? result.StdErr : result.StdOut;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var status = root.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
            return (id, status);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
