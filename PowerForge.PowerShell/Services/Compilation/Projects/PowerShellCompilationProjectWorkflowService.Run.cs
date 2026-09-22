namespace PowerForge;

public sealed partial class PowerShellCompilationProjectWorkflowService
{
    /// <summary>
    /// Builds and verifies one declared executable, then runs it from the project directory.
    /// Stdin is inherited; stdout/stderr are inherited unless capture is requested.
    /// Cancellation owns the foreground process tree and never launches a canceled build.
    /// </summary>
    public async Task<PowerShellCompilationProjectRunResult> RunAsync(
        string projectPath,
        PowerShellCompilationProjectRunOptions? options = null,
        Action<PowerShellCompilationProjectRunEvent>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var settings = CopyRunOptions(options);
        var context = PowerShellCompilationProjectManifestService.Open(projectPath);
        using var lease = AcquireDevelopmentLease(context);
        return await RunCoreAsync(context.ProjectPath, settings, progress, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PowerShellCompilationProjectRunResult> RunCoreAsync(
        string projectPath,
        PowerShellCompilationProjectRunOptions options,
        Action<PowerShellCompilationProjectRunEvent>? progress,
        CancellationToken cancellationToken)
    {
        var result = new PowerShellCompilationProjectRunResult();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = PowerShellCompilationProjectManifestService.Open(projectPath);
            var artifacts = SelectArtifacts(context, options.TargetName is null ? null : new[] { options.TargetName });
            if (artifacts.Length != 1)
                throw new InvalidOperationException("Run/watch requires exactly one target; select it with --target.");
            var artifact = artifacts[0];
            if (artifact.Target.ArtifactKind != PowerShellCompilationArtifactKind.Executable)
                throw new InvalidOperationException("Run/watch requires an executable target. Use project test for library or module targets.");
            EnsureCurrentTargetHost(artifact.Target);
            var inputs = new DevelopmentInputs(context);
            var inputIdentity = inputs.Capture(cancellationToken);
            Report("building", "Building " + artifact.Name + " from the current source and reviewed dependency closure.");
            result.Build = await Task.Run(() => BuildCore(projectPath, new[] { artifact.Name },
                development: true, cancellationToken), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!result.Build.Succeeded)
                throw new InvalidOperationException(string.Join(Environment.NewLine, result.Build.Targets.Select(target => target.Message)));

            // Reopen after building: changed project metadata or source must not validate against
            // the context read before the build. The verifier checks current input and output hashes.
            var current = PowerShellCompilationProjectManifestService.Open(projectPath);
            var selected = SelectArtifacts(current, new[] { artifact.Name }).Single();
            var validated = ValidateBuildReceipt(current, selected, development: true);
            cancellationToken.ThrowIfCancellationRequested();
            Report("starting", "Starting " + selected.Name + ".");
            var request = new ProcessRunRequest(
                validated.ArtifactPath, current.Root, options.Arguments, Timeout.InfiniteTimeSpan,
                environmentVariables: null, captureOutput: options.CaptureOutput, captureError: options.CaptureError);
            request.SetPreStartBoundary(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                inputs.RefreshContext();
                if (!inputIdentity.Equals(inputs.Capture(cancellationToken), StringComparison.Ordinal))
                    throw new InvalidOperationException("Project inputs changed during the build; the superseded artifact was not run. Retry run or let watch rebuild.");
                // Keep this attempt bound to the inventory authenticated above. Reading a new
                // receipt here could silently accept another producer's replacement artifact.
                PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(validated.OutputRoot,
                    "Artifact output traverses a symbolic link or junction.");
                var launchFiles = Directory.EnumerateFiles(validated.OutputRoot, "*", SearchOption.AllDirectories)
                    .Where(path => IsArtifactPayloadFile(path, current, selected))
                    .Select(path => CreateFileEvidence(validated.OutputRoot, path))
                    .OrderBy(static file => file.Path, StringComparer.Ordinal)
                    .ToArray();
                if (!FileInventoriesEqual(validated.Files, launchFiles))
                    throw new InvalidDataException("Artifact output changed before launch; the replacement artifact was not run: " +
                        DescribeFileInventoryDifference(validated.Files, launchFiles));
                cancellationToken.ThrowIfCancellationRequested();
            });
            request.SetStartBoundary(() => Report("running", "Running " + selected.Name + "."));
            result.Process = await new ProcessRunner(ownProcessTree: true).RunAsync(request, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Process.StartFailed)
                throw new InvalidOperationException(result.Process.StdErr);
            Report("exited", "Application exited with code " + result.Process.ExitCode + ".", result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            result.Error = exception.Message;
            Report("failed", exception.Message, result);
        }
        return result;

        void Report(string state, string message, PowerShellCompilationProjectRunResult? completed = null)
            => progress?.Invoke(new PowerShellCompilationProjectRunEvent { State = state, Message = message, Result = completed });
    }

    private static FileStream AcquireDevelopmentLease(PowerShellCompilationProjectManifestService.ProjectContext context)
    {
        var path = context.Resolve(".powerforge/development.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException exception)
        {
            throw new IOException("Another run/watch operation owns this project. Stop it before starting another development session.", exception);
        }
    }

    private static PowerShellCompilationProjectRunOptions CopyRunOptions(PowerShellCompilationProjectRunOptions? options)
    {
        options ??= new PowerShellCompilationProjectRunOptions();
        if (options.Arguments is null || options.Arguments.Any(argument => argument is null || argument.IndexOf('\0') >= 0))
            throw new ArgumentException("Application arguments cannot be null or contain NUL.", nameof(options));
        if (options.PollIntervalMilliseconds < 20 || options.DebounceMilliseconds < 0)
            throw new ArgumentException("Watch polling must be at least 20ms and debounce must be nonnegative.", nameof(options));
        return new PowerShellCompilationProjectRunOptions
        {
            TargetName = string.IsNullOrWhiteSpace(options.TargetName) ? null : options.TargetName,
            Arguments = options.Arguments.ToArray(),
            CaptureOutput = options.CaptureOutput,
            CaptureError = options.CaptureError,
            PollIntervalMilliseconds = options.PollIntervalMilliseconds,
            DebounceMilliseconds = options.DebounceMilliseconds
        };
    }
}
