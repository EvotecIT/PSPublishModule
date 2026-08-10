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

        var xcrunExecutable = ResolveAppleToolExecutable(
            request.XcrunExecutable,
            "xcrun",
            "/usr/bin/xcrun",
            request.RequireTrustedSystemTools);
        var dittoExecutable = ResolveAppleToolExecutable(
            request.DittoExecutable,
            "ditto",
            "/usr/bin/ditto",
            request.RequireTrustedSystemTools);
        var spctlExecutable = ResolveAppleToolExecutable(
            request.SpctlExecutable,
            "spctl",
            "/usr/sbin/spctl",
            request.RequireTrustedSystemTools);
        var toolEnvironment = request.RequireTrustedSystemTools
            ? AppleTrustedExecutionEnvironment.Create()
            : null;

        var artifactSha256 = ComputeArtifactSha256(artifactPath);
        var expectedArtifactSha256 = string.IsNullOrWhiteSpace(request.ExpectedArtifactSha256)
            ? null
            : request.ExpectedArtifactSha256!.Trim();
        var artifactChangedSinceCheckpoint = expectedArtifactSha256 is not null &&
                                             !artifactSha256.Equals(expectedArtifactSha256, StringComparison.OrdinalIgnoreCase);
        if (artifactChangedSinceCheckpoint)
        {
            throw new InvalidOperationException(
                $"The direct Apple artifact changed after its last trusted notarization checkpoint. Expected SHA-256 " +
                $"'{expectedArtifactSha256}', received '{artifactSha256}'. A stapler validation alone cannot prove artifact identity; " +
                "re-export and reconcile the accepted submission before retrying.");
        }

        var timeout = request.Timeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(30) : request.Timeout;
        var resumed = !string.IsNullOrWhiteSpace(request.AcceptedSubmissionId);
        if (request.StaplingCompleted && !resumed)
            throw new ArgumentException("StaplingCompleted requires AcceptedSubmissionId.", nameof(request));
        var staplingCompleted = request.StaplingCompleted;
        using var submissionSnapshot = AppleNotarizationInputSnapshot.Create(artifactPath, artifactSha256);
        var submissionArtifactPath = submissionSnapshot.ArtifactPath;
        using var packagingMonitor = !resumed &&
                                     extension.Equals(".app", StringComparison.OrdinalIgnoreCase)
            ? new AppleReleaseSourceMutationMonitor(
                submissionArtifactPath,
                "private Apple notarization app snapshot",
                "ditto",
                "Discard the package and create a new notarization snapshot.")
            : null;
        var submittedPath = resumed
            ? artifactPath
            : await PrepareSubmissionAsync(
                    request,
                    dittoExecutable,
                    toolEnvironment,
                    submissionArtifactPath,
                    submissionSnapshot.RootPath,
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
        if (packagingMonitor is not null)
        {
            packagingMonitor.ValidateNoChanges();
            var observedPackagedArtifactSha256 = ComputeArtifactSha256(submissionArtifactPath);
            if (!observedPackagedArtifactSha256.Equals(artifactSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The private Apple notarization app snapshot changed while ditto was packaging it. Expected SHA-256 " +
                    $"'{artifactSha256}', received '{observedPackagedArtifactSha256}'. Discard the package and create a new notarization snapshot.");
            }
        }
        var submissionSha256 = resumed
            ? request.AcceptedSubmissionSha256?.Trim()
            : ComputeFileSha256(submittedPath);
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
                "notarytool", "submit", submittedPath, "--wait", "--output-format", "json"
            };
            submitArguments.AddRange(authentication);
            using var submissionMonitor = new AppleReleaseSourceMutationMonitor(
                submissionSnapshot.RootPath,
                "private Apple notarization submission",
                "notarytool",
                "Do not resubmit until the accepted submission has been reconciled.");
            submissionSnapshot.CompleteSubmissionCapture(artifactSha256);
            submission = await RunAsync(xcrunExecutable, submissionArtifactPath, submitArguments, timeout, toolEnvironment, cancellationToken).ConfigureAwait(false);
            (submissionId, status) = ParseSubmission(submission);
            try
            {
                submissionMonitor.ValidateNoChanges();
                var observedSubmissionSha256 = ComputeFileSha256(submittedPath);
                if (!observedSubmissionSha256.Equals(submissionSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"The private Apple notarization submission changed during notarytool execution. Expected SHA-256 " +
                        $"'{submissionSha256}', received '{observedSubmissionSha256}'.");
                }
            }
            catch (Exception ex) when (
                submission.Succeeded &&
                string.Equals(status, "Accepted", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(submissionId))
            {
                throw new InvalidOperationException(
                    $"Apple accepted notarization submission '{submissionId}', but the exact submitted file changed while notarytool was reading it. " +
                    "Do not resubmit until the accepted submission has been reconciled.",
                    ex);
            }
        }
        var submissionPath = resumed
            ? artifactPath
            : ResolveRetainedSubmissionPath(request, artifactPath);

        if (!resumed &&
            submission.Succeeded &&
            string.Equals(status, "Accepted", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(submissionId))
        {
            try
            {
                request.AcceptedCheckpoint?.Invoke(new AppleNotarizationAcceptedCheckpoint
                {
                    ArtifactPath = artifactPath,
                    ArtifactSha256 = artifactSha256,
                    SubmissionPath = submissionPath,
                    SubmissionSha256 = submissionSha256!,
                    SubmissionId = submissionId!,
                    Status = status!
                });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Apple accepted notarization submission '{submissionId}', but its local recovery checkpoint could not be persisted. " +
                    "Do not resubmit the artifact until the accepted submission has been reconciled.",
                    ex);
            }
        }

        if (!resumed)
            PreserveSubmissionPath(artifactPath, submittedPath, submissionPath, submissionSha256!);

        ProcessRunResult? staple = null;
        ProcessRunResult? validation = null;
        ProcessRunResult? assessment = null;
        var stapledThisInvocation = false;
        string? validatedStapledArtifactSha256 = null;
        if (submission.Succeeded && string.Equals(status, "Accepted", StringComparison.OrdinalIgnoreCase) && request.Staple)
        {
            if (staplingCompleted)
            {
                staple = new ProcessRunResult(
                    0,
                    "Skipped stapling because a retained exact post-staple checkpoint proves that it already succeeded.",
                    string.Empty,
                    xcrunExecutable,
                    TimeSpan.Zero,
                    false);
            }
            else
            {
                staple = await RunAsync(xcrunExecutable, submissionArtifactPath, new[] { "stapler", "staple", submissionArtifactPath }, timeout, toolEnvironment, cancellationToken).ConfigureAwait(false);
                stapledThisInvocation = staple.Succeeded;
            }
        }
        using var postStapleMonitor = staple?.Succeeded == true
            ? new AppleReleaseSourceMutationMonitor(
                submissionSnapshot.RootPath,
                "validated private Apple notarization artifact",
                "stapler validation, Gatekeeper assessment, and final publication",
                "Discard the private artifact and resume from the last durable notarization checkpoint.")
            : null;
        if (staple?.Succeeded == true)
        {
            validation = await RunAsync(
                    xcrunExecutable,
                    submissionArtifactPath,
                    new[] { "stapler", "validate", submissionArtifactPath },
                    timeout,
                    toolEnvironment,
                    cancellationToken)
                .ConfigureAwait(false);
            if (validation.Succeeded)
                validatedStapledArtifactSha256 = ComputeArtifactSha256(submissionArtifactPath);
        }
        if (submission.Succeeded && string.Equals(status, "Accepted", StringComparison.OrdinalIgnoreCase) && request.Assess)
        {
            var assessmentArguments = extension.Equals(".dmg", StringComparison.OrdinalIgnoreCase)
                ? new[] { "--assess", "--type", "open", "--context", "context:primary-signature", "--verbose=4", submissionArtifactPath }
                : new[]
                {
                    "--assess", "--type",
                    extension.Equals(".app", StringComparison.OrdinalIgnoreCase) ? "execute" : "install",
                    "--verbose=4", submissionArtifactPath
                };
            assessment = await RunAsync(spctlExecutable, submissionArtifactPath, assessmentArguments, timeout, toolEnvironment, cancellationToken).ConfigureAwait(false);
        }

        string finalArtifactSha256;
        if (request.Staple &&
            staple?.Succeeded == true &&
            validation?.Succeeded == true &&
            validatedStapledArtifactSha256 is not null)
        {
            postStapleMonitor?.ValidateNoChanges();
            finalArtifactSha256 = submissionSnapshot.PublishTo(artifactPath, validatedStapledArtifactSha256);
            if (stapledThisInvocation && !string.IsNullOrWhiteSpace(submissionId))
            {
                try
                {
                    request.StapledCheckpoint?.Invoke(new AppleNotarizationStapledCheckpoint
                    {
                        ArtifactPath = artifactPath,
                        ArtifactSha256 = finalArtifactSha256,
                        SubmissionSha256 = submissionSha256 ?? string.Empty,
                        SubmissionId = submissionId!,
                        Status = status ?? "Accepted"
                    });
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Apple notarization submission '{submissionId}' was stapled and validated, but its local recovery checkpoint could not be persisted. " +
                        "Do not replace or resubmit the artifact until the stapled submission has been reconciled.",
                        ex);
                }
            }
        }
        else
        {
            finalArtifactSha256 = ComputeArtifactSha256(artifactPath);
            if (!finalArtifactSha256.Equals(artifactSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The public Apple notarization artifact changed while its private snapshot was being processed. Expected '{artifactSha256}', received '{finalArtifactSha256}'.");
            }
        }

        return new AppleNotarizationResult
        {
            ArtifactPath = artifactPath,
            ArtifactSha256 = finalArtifactSha256,
            SubmissionPath = submissionPath,
            SubmissionSha256 = submissionSha256,
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
        string dittoExecutable,
        IReadOnlyDictionary<string, string?>? toolEnvironment,
        string artifactPath,
        string privateRoot,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!Path.GetExtension(artifactPath).Equals(".app", StringComparison.OrdinalIgnoreCase))
            return artifactPath;

        var submissionPath = Path.Combine(
            privateRoot,
            Path.GetFileNameWithoutExtension(artifactPath) + ".notarization.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(submissionPath)!);
        var package = await RunAsync(
            dittoExecutable,
            artifactPath,
            new[] { "-c", "-k", "--keepParent", artifactPath, submissionPath },
            timeout,
            toolEnvironment,
            cancellationToken).ConfigureAwait(false);
        if (!package.Succeeded)
            throw new InvalidOperationException($"ditto failed to package '{artifactPath}' for notarization with exit code {package.ExitCode}: {package.StdErr}");
        return submissionPath;
    }

    private static string ResolveRetainedSubmissionPath(
        AppleNotarizationRequest request,
        string originalArtifactPath)
    {
        if (!Path.GetExtension(originalArtifactPath).Equals(".app", StringComparison.OrdinalIgnoreCase))
            return originalArtifactPath;

        return string.IsNullOrWhiteSpace(request.SubmissionPath)
            ? Path.Combine(
                Path.GetDirectoryName(originalArtifactPath)!,
                Path.GetFileNameWithoutExtension(originalArtifactPath) + ".notarization.zip")
            : Path.GetFullPath(request.SubmissionPath!);
    }

    private static void PreserveSubmissionPath(
        string originalArtifactPath,
        string submittedPath,
        string retainedPath,
        string expectedSubmissionSha256)
    {
        if (!Path.GetExtension(originalArtifactPath).Equals(".app", StringComparison.OrdinalIgnoreCase))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(retainedPath)!);
        File.Copy(submittedPath, retainedPath, overwrite: true);
        var retainedSha256 = ComputeFileSha256(retainedPath);
        if (!retainedSha256.Equals(expectedSubmissionSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The retained Apple notarization submission does not match the exact accepted file. Expected SHA-256 " +
                $"'{expectedSubmissionSha256}', received '{retainedSha256}'.");
        }
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
        IReadOnlyDictionary<string, string?>? environmentVariables,
        CancellationToken cancellationToken)
        => _processRunner.RunAsync(
            new ProcessRunRequest(
                executable,
                Path.GetDirectoryName(artifactPath) ?? Directory.GetCurrentDirectory(),
                arguments,
                timeout,
                environmentVariables,
                captureOutput: true,
                captureError: true,
                inheritEnvironment: environmentVariables is null),
            cancellationToken);

    private static string ResolveAppleToolExecutable(
        string? executable,
        string defaultName,
        string trustedPath,
        bool requireTrustedSystemTool)
    {
        var value = string.IsNullOrWhiteSpace(executable)
            ? defaultName
            : executable!.Trim();
        if (!requireTrustedSystemTool)
            return value;
        if (value.Equals(defaultName, StringComparison.Ordinal) ||
            value.Equals(trustedPath, StringComparison.Ordinal))
        {
            return trustedPath;
        }

        throw new InvalidOperationException(
            $"Exact-source Apple notarization requires the trusted system tool '{trustedPath}'; received '{value}'.");
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

    internal static string ComputeArtifactSha256(string artifactPath)
    {
        using var sha256 = SHA256.Create();
        AppendValue(sha256, "PowerForge.ArtifactSha256.v2");
        if (File.Exists(artifactPath))
        {
            AppendFileSystemEntry(
                sha256,
                new FileInfo(artifactPath),
                Path.GetFileName(artifactPath),
                includeContents: true);
        }
        else
        {
            var root = new DirectoryInfo(artifactPath);
            AppendFileSystemEntry(sha256, root, ".", includeContents: false);
            var entries = new List<(FileSystemInfo Info, string RelativePath)>();
            var pending = new Stack<DirectoryInfo>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                foreach (var entry in directory.EnumerateFileSystemInfos())
                {
                    var relativePath = FrameworkCompatibility.GetRelativePath(artifactPath, entry.FullName)
                        .Replace('\\', '/');
                    entries.Add((entry, relativePath));
                    if ((entry.Attributes & FileAttributes.Directory) != 0 &&
                        (entry.Attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push((DirectoryInfo)entry);
                    }
                }
            }

            foreach (var entry in entries.OrderBy(static item => item.RelativePath, StringComparer.Ordinal))
            {
                var includeContents = (entry.Info.Attributes & FileAttributes.Directory) == 0 &&
                                      (entry.Info.Attributes & FileAttributes.ReparsePoint) == 0;
                AppendFileSystemEntry(
                    sha256,
                    entry.Info,
                    entry.RelativePath,
                    includeContents);
            }
        }

        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return BitConverter.ToString(sha256.Hash!).Replace("-", string.Empty).ToLowerInvariant();
    }

    internal static string ComputeFileSha256(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static void AppendFileSystemEntry(
        HashAlgorithm hash,
        FileSystemInfo entry,
        string relativePath,
        bool includeContents)
    {
        AppendValue(hash, "entry");
        AppendValue(hash, relativePath);
        AppendValue(hash, ((int)entry.Attributes).ToString(System.Globalization.CultureInfo.InvariantCulture));
#if NET8_0_OR_GREATER
        if (OperatingSystem.IsWindows())
        {
            AppendValue(hash, string.Empty);
        }
        else
        {
            AppendValue(
                hash,
                ((int)File.GetUnixFileMode(entry.FullName))
                    .ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        AppendValue(hash, entry.LinkTarget ?? string.Empty);
#else
        AppendValue(hash, string.Empty);
        AppendValue(hash, string.Empty);
#endif
        AppendValue(hash, includeContents ? "file" : "metadata");
        if (includeContents)
            AppendFile(hash, entry.FullName);
    }

    private static void AppendValue(HashAlgorithm hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        AppendLength(hash, bytes.LongLength);
        AppendBytes(hash, bytes);
    }

    private static void AppendFile(HashAlgorithm hash, string path)
    {
        using var stream = File.OpenRead(path);
        AppendLength(hash, stream.Length);
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            hash.TransformBlock(buffer, 0, read, buffer, 0);
    }

    private static void AppendLength(HashAlgorithm hash, long value)
    {
        var bytes = new byte[sizeof(long)];
        for (var index = bytes.Length - 1; index >= 0; index--)
        {
            bytes[index] = (byte)(value & 0xff);
            value >>= 8;
        }
        AppendBytes(hash, bytes);
    }

    private static void AppendBytes(HashAlgorithm hash, byte[] bytes)
    {
        if (bytes.Length > 0)
            hash.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
    }
}
