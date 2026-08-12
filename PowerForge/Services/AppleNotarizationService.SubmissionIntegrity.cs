using System.Text.Json;

namespace PowerForge;

public sealed partial class AppleNotarizationService
{
    private static InvalidOperationException CreateAmbiguousSubmissionException(
        AppleNotarizationRequest request,
        string artifactPath,
        string artifactSha256,
        string submissionPath,
        string submissionSha256,
        string? submissionId,
        string? status,
        Exception? processException = null)
    {
        try
        {
            request.AmbiguousCheckpoint?.Invoke(new AppleNotarizationAmbiguousCheckpoint
            {
                ArtifactPath = artifactPath,
                ArtifactSha256 = artifactSha256,
                SubmissionPath = submissionPath,
                SubmissionSha256 = submissionSha256,
                SubmissionId = submissionId,
                Status = status
            });
        }
        catch (Exception checkpointException)
        {
            return new InvalidOperationException(
                "The notarytool submission attempt ended without definitive terminal evidence, and the ambiguous remote mutation checkpoint could not be persisted. " +
                "Do not resubmit until Apple notary history has been reconciled.",
                new AggregateException(
                    new[] { processException, checkpointException }.Where(static value => value is not null).Cast<Exception>()));
        }

        return new InvalidOperationException(
            "The notarytool submission attempt ended without a complete terminal submission id and status. The remote mutation is ambiguous; " +
            "do not resubmit until Apple notary history has been reconciled.",
            processException);
    }

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
