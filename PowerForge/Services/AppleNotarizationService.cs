using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>Submits direct macOS artifacts for notarization and verifies the accepted result locally.</summary>
public sealed class AppleNotarizationService
{
    private readonly IProcessRunner _processRunner;

    /// <summary>Creates a notarization service.</summary>
    public AppleNotarizationService(IProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new ProcessRunner();
    }

    /// <summary>Notarizes, staples, validates, and assesses an artifact.</summary>
    public async Task<AppleNotarizationResult> NotarizeAsync(
        AppleNotarizationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.ArtifactPath))
            throw new ArgumentException("ArtifactPath is required.", nameof(request));

        var artifactPath = Path.GetFullPath(request.ArtifactPath);
        if (!File.Exists(artifactPath) && !Directory.Exists(artifactPath))
            throw new FileNotFoundException("Apple notarization artifact was not found.", artifactPath);

        var extension = Path.GetExtension(artifactPath);
        if (!extension.Equals(".app", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".dmg", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".pkg", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "ArtifactPath must be an .app bundle, .dmg disk image, or signed flat .pkg installer. " +
                "ZIP archives can be submitted to Apple but cannot be stapled directly; submit the contained app instead.",
                nameof(request));
        }

        var artifactSha256 = ComputeArtifactSha256(artifactPath);
        var expectedArtifactSha256 = string.IsNullOrWhiteSpace(request.ExpectedArtifactSha256)
            ? null
            : request.ExpectedArtifactSha256!.Trim();
        if (expectedArtifactSha256 is not null &&
            !artifactSha256.Equals(expectedArtifactSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The direct Apple artifact changed after notarization acceptance. Expected SHA-256 " +
                $"'{expectedArtifactSha256}', received '{artifactSha256}'. Archive, export, and submit the changed artifact as a new release attempt.");
        }

        var timeout = request.Timeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(30) : request.Timeout;
        var resumed = !string.IsNullOrWhiteSpace(request.AcceptedSubmissionId);
        if (request.StaplingCompleted && !resumed)
            throw new ArgumentException("StaplingCompleted requires AcceptedSubmissionId.", nameof(request));
        var submissionPath = resumed
            ? artifactPath
            : await PrepareSubmissionAsync(request, artifactPath, timeout, cancellationToken).ConfigureAwait(false);
        ProcessRunResult submission;
        string? submissionId;
        string? status;
        if (resumed)
        {
            submissionId = request.AcceptedSubmissionId!.Trim();
            status = "Accepted";
            submission = new ProcessRunResult(
                0,
                "Resumed previously accepted Apple notarization submission.",
                string.Empty,
                request.XcrunExecutable,
                TimeSpan.Zero,
                false);
        }
        else
        {
            var authentication = BuildAuthenticationArguments(request);
            var submitArguments = new List<string>
            {
                "notarytool", "submit", submissionPath, "--wait", "--output-format", "json"
            };
            submitArguments.AddRange(authentication);
            submission = await RunAsync(request.XcrunExecutable, artifactPath, submitArguments, timeout, cancellationToken).ConfigureAwait(false);
            (submissionId, status) = ParseSubmission(submission);
        }

        ProcessRunResult? staple = null;
        ProcessRunResult? validation = null;
        ProcessRunResult? assessment = null;
        if (submission.Succeeded && string.Equals(status, "Accepted", StringComparison.OrdinalIgnoreCase) && request.Staple)
        {
            if (request.StaplingCompleted)
            {
                staple = new ProcessRunResult(
                    0,
                    "Skipped stapling because the retained receipt records that it already succeeded.",
                    string.Empty,
                    request.XcrunExecutable,
                    TimeSpan.Zero,
                    false);
            }
            else
            {
                staple = await RunAsync(request.XcrunExecutable, artifactPath, new[] { "stapler", "staple", artifactPath }, timeout, cancellationToken).ConfigureAwait(false);
            }
            if (staple?.Succeeded == true)
                validation = await RunAsync(request.XcrunExecutable, artifactPath, new[] { "stapler", "validate", artifactPath }, timeout, cancellationToken).ConfigureAwait(false);
        }
        if (submission.Succeeded && string.Equals(status, "Accepted", StringComparison.OrdinalIgnoreCase) && request.Assess)
        {
            var assessmentArguments = extension.Equals(".dmg", StringComparison.OrdinalIgnoreCase)
                ? new[] { "--assess", "--type", "open", "--context", "context:primary-signature", "--verbose=4", artifactPath }
                : new[]
                {
                    "--assess", "--type",
                    extension.Equals(".app", StringComparison.OrdinalIgnoreCase) ? "execute" : "install",
                    "--verbose=4", artifactPath
                };
            assessment = await RunAsync(request.SpctlExecutable, artifactPath, assessmentArguments, timeout, cancellationToken).ConfigureAwait(false);
        }

        return new AppleNotarizationResult
        {
            ArtifactPath = artifactPath,
            ArtifactSha256 = ComputeArtifactSha256(artifactPath),
            SubmissionPath = submissionPath,
            SubmissionId = submissionId,
            Status = status,
            ResumedAcceptedSubmission = resumed,
            Submission = submission,
            Staple = staple,
            StapleValidation = validation,
            Assessment = assessment
        };
    }

    private async Task<string> PrepareSubmissionAsync(
        AppleNotarizationRequest request,
        string artifactPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!Path.GetExtension(artifactPath).Equals(".app", StringComparison.OrdinalIgnoreCase))
            return artifactPath;

        var submissionPath = string.IsNullOrWhiteSpace(request.SubmissionPath)
            ? Path.Combine(Path.GetDirectoryName(artifactPath)!, Path.GetFileNameWithoutExtension(artifactPath) + ".notarization.zip")
            : Path.GetFullPath(request.SubmissionPath!);
        Directory.CreateDirectory(Path.GetDirectoryName(submissionPath)!);
        var package = await RunAsync(
            request.DittoExecutable,
            artifactPath,
            new[] { "-c", "-k", "--keepParent", artifactPath, submissionPath },
            timeout,
            cancellationToken).ConfigureAwait(false);
        if (!package.Succeeded)
            throw new InvalidOperationException($"ditto failed to package '{artifactPath}' for notarization with exit code {package.ExitCode}: {package.StdErr}");
        return submissionPath;
    }

    private static string[] BuildAuthenticationArguments(AppleNotarizationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.KeychainProfile))
            return new[] { "--keychain-profile", request.KeychainProfile!.Trim() };

        var values = new[] { request.ApiKeyPath, request.ApiKeyId, request.ApiIssuerId };
        if (values.Any(static value => string.IsNullOrWhiteSpace(value)))
            throw new ArgumentException("Notarization requires KeychainProfile or ApiKeyPath, ApiKeyId, and ApiIssuerId.", nameof(request));
        var keyPath = Path.GetFullPath(request.ApiKeyPath!);
        if (!File.Exists(keyPath))
            throw new FileNotFoundException("Notarization API private key was not found.", keyPath);
        return new[] { "--key", keyPath, "--key-id", request.ApiKeyId!.Trim(), "--issuer", request.ApiIssuerId!.Trim() };
    }

    private Task<ProcessRunResult> RunAsync(
        string executable,
        string artifactPath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        => _processRunner.RunAsync(
            new ProcessRunRequest(
                string.IsNullOrWhiteSpace(executable) ? "xcrun" : executable.Trim(),
                Path.GetDirectoryName(artifactPath) ?? Directory.GetCurrentDirectory(),
                arguments,
                timeout),
            cancellationToken);

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

    private static string ComputeArtifactSha256(string artifactPath)
    {
        using var sha256 = SHA256.Create();
        if (File.Exists(artifactPath))
        {
            AppendFile(sha256, artifactPath);
        }
        else
        {
            var files = Directory.EnumerateFiles(artifactPath, "*", SearchOption.AllDirectories)
                .Select(path => new
                {
                    FullPath = path,
                    RelativePath = FrameworkCompatibility.GetRelativePath(artifactPath, path).Replace('\\', '/')
                })
                .OrderBy(static item => item.RelativePath, StringComparer.Ordinal)
                .ToArray();
            foreach (var file in files)
            {
                AppendBytes(sha256, Encoding.UTF8.GetBytes(file.RelativePath));
                AppendBytes(sha256, new byte[] { 0 });
                AppendFile(sha256, file.FullPath);
                AppendBytes(sha256, new byte[] { 0 });
            }
        }

        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return BitConverter.ToString(sha256.Hash!).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static void AppendFile(HashAlgorithm hash, string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            hash.TransformBlock(buffer, 0, read, buffer, 0);
    }

    private static void AppendBytes(HashAlgorithm hash, byte[] bytes)
        => hash.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
}
