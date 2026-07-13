namespace PowerForge;

/// <summary>
/// Reusable typed client for <c>dotnet nuget</c> operations.
/// </summary>
public sealed class DotNetNuGetClient
{
    private readonly IProcessRunner _processRunner;
    private readonly string _dotNetExecutable;
    private readonly TimeSpan _defaultTimeout;
    private readonly string _runtimeDirectoryRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="DotNetNuGetClient"/> class.
    /// </summary>
    /// <param name="processRunner">Optional process runner implementation.</param>
    /// <param name="dotNetExecutable">Optional dotnet executable name or path.</param>
    /// <param name="defaultTimeout">Optional default timeout.</param>
    /// <param name="runtimeDirectoryRoot">Optional runtime directory root for temporary response files.</param>
    public DotNetNuGetClient(
        IProcessRunner? processRunner = null,
        string dotNetExecutable = "dotnet",
        TimeSpan? defaultTimeout = null,
        string? runtimeDirectoryRoot = null)
    {
        _processRunner = processRunner ?? new ProcessRunner();
        _dotNetExecutable = string.IsNullOrWhiteSpace(dotNetExecutable) ? "dotnet" : dotNetExecutable;
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromMinutes(10);
        _runtimeDirectoryRoot = NormalizeRuntimeDirectoryRoot(runtimeDirectoryRoot);
    }

    /// <summary>
    /// Executes <c>dotnet nuget push</c> using a temporary response file.
    /// </summary>
    /// <param name="request">Push request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured push result.</returns>
    public async Task<DotNetNuGetPushResult> PushPackageAsync(DotNetNuGetPushRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.PackagePath))
            throw new ArgumentException("PackagePath is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ApiKey))
            throw new ArgumentException("ApiKey is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Source))
            throw new ArgumentException("Source is required.", nameof(request));

        Directory.CreateDirectory(_runtimeDirectoryRoot);
        var responseFilePath = Path.Combine(_runtimeDirectoryRoot, $"nuget-push-{Guid.NewGuid():N}.rsp");
        PushExecutionContext? pushContext = null;

        try
        {
            pushContext = PreparePushExecutionContext(request);
            File.WriteAllText(
                responseFilePath,
                BuildPushResponseFileContent(
                    request,
                    pushContext.Value.PackagePath,
                    pushContext.Value.Source));

            var processResult = await _processRunner.RunAsync(
                new ProcessRunRequest(
                    _dotNetExecutable,
                    pushContext.Value.WorkingDirectory,
                    [$"@{responseFilePath}"],
                    request.Timeout ?? _defaultTimeout),
                cancellationToken).ConfigureAwait(false);

            return new DotNetNuGetPushResult(
                processResult.ExitCode,
                processResult.StdOut,
                processResult.StdErr,
                processResult.Executable,
                processResult.Duration,
                processResult.TimedOut,
                processResult.ExitCode == 0 && !processResult.TimedOut
                    ? null
                    : FirstLine(processResult.StdErr) ?? FirstLine(processResult.StdOut) ?? $"dotnet nuget push failed with exit code {processResult.ExitCode}.");
        }
        finally
        {
            TryDeleteFile(responseFilePath);
            if (pushContext?.StagingDirectory is string stagingDirectory)
                TryDeleteDirectory(stagingDirectory);
        }
    }

    /// <summary>
    /// Executes <c>dotnet nuget sign</c>.
    /// </summary>
    /// <param name="request">Sign request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured sign result.</returns>
    public async Task<DotNetNuGetSignResult> SignPackageAsync(DotNetNuGetSignRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.PackagePaths.Length == 0)
            throw new ArgumentException("At least one package path is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.CertificateFingerprint))
            throw new ArgumentException("CertificateFingerprint is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.CertificateStoreLocation))
            throw new ArgumentException("CertificateStoreLocation is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.CertificateStoreName))
            throw new ArgumentException("CertificateStoreName is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.TimeStampServer))
            throw new ArgumentException("TimeStampServer is required.", nameof(request));

        var arguments = new List<string> {
            "nuget",
            "sign"
        };
        arguments.AddRange(request.PackagePaths);
        arguments.AddRange(new[]
        {
            "--certificate-fingerprint",
            request.CertificateFingerprint,
            "--certificate-store-location",
            request.CertificateStoreLocation,
            "--certificate-store-name",
            request.CertificateStoreName,
            "--timestamper",
            request.TimeStampServer
        });

        if (request.Overwrite)
            arguments.Add("--overwrite");

        var processResult = await _processRunner.RunAsync(
            new ProcessRunRequest(
                _dotNetExecutable,
                ResolveWorkingDirectory(request.WorkingDirectory, request.PackagePath),
                arguments,
                request.Timeout ?? _defaultTimeout),
            cancellationToken).ConfigureAwait(false);

        return new DotNetNuGetSignResult(
            processResult.ExitCode,
            processResult.StdOut,
            processResult.StdErr,
            processResult.Executable,
            processResult.Duration,
            processResult.TimedOut,
            processResult.ExitCode == 0 && !processResult.TimedOut
                ? null
                : FirstLine(processResult.StdErr) ?? FirstLine(processResult.StdOut) ?? $"dotnet nuget sign failed with exit code {processResult.ExitCode}.");
    }

    private static string BuildPushResponseFileContent(
        DotNetNuGetPushRequest request,
        string packagePath,
        string source)
    {
        // The .NET CLI keeps quotes as literal characters when one response-file token is written per line.
        // Keep whitespace-bearing values raw so paths and keys remain single tokens without embedded quotes.
        var lines = new List<string> {
            "nuget",
            "push",
            packagePath,
            "--api-key",
            request.ApiKey,
            "--source",
            source
        };

        if (request.SkipDuplicate)
            lines.Add("--skip-duplicate");
        if (request.SuppressCompanionSymbols)
            lines.Add("--no-symbols");

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Keeps the NuGet.config lookup context while placing an implicit companion symbol package beside
    /// the primary package in the process working directory, as required by <c>dotnet nuget push</c>.
    /// </summary>
    private static PushExecutionContext PreparePushExecutionContext(DotNetNuGetPushRequest request)
    {
        var configurationDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
            ? Environment.CurrentDirectory
            : PathValueResolver.Resolve(Environment.CurrentDirectory, request.WorkingDirectory!);
        var packagePath = PathValueResolver.Resolve(
            string.IsNullOrWhiteSpace(request.WorkingDirectory)
                ? Environment.CurrentDirectory
                : configurationDirectory,
            request.PackagePath);
        var workingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
            ? Path.GetDirectoryName(packagePath) ?? configurationDirectory
            : configurationDirectory;
        var source = ResolvePushSource(request.Source, workingDirectory);
        if (request.SuppressCompanionSymbols ||
            string.IsNullOrWhiteSpace(request.WorkingDirectory) ||
            packagePath.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase) ||
            !packagePath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            return new PushExecutionContext(packagePath, source, workingDirectory, stagingDirectory: null);
        }

        var symbolPackagePath = Path.ChangeExtension(packagePath, ".snupkg");
        if (!File.Exists(symbolPackagePath))
            return new PushExecutionContext(packagePath, source, workingDirectory, stagingDirectory: null);

        if (!Directory.Exists(configurationDirectory))
            throw new DirectoryNotFoundException($"NuGet push working directory not found: {configurationDirectory}");

        var packageDirectory = Path.GetDirectoryName(packagePath);
        if (!string.IsNullOrWhiteSpace(packageDirectory) &&
            IsSameOrChildDirectory(packageDirectory!, configurationDirectory))
        {
            return new PushExecutionContext(packagePath, source, packageDirectory!, stagingDirectory: null);
        }

        var stagingDirectory = Path.Combine(
            configurationDirectory,
            $".powerforge-nuget-push-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            var stagedPackagePath = Path.Combine(stagingDirectory, Path.GetFileName(packagePath));
            var stagedSymbolPackagePath = Path.Combine(stagingDirectory, Path.GetFileName(symbolPackagePath));
            File.Copy(packagePath, stagedPackagePath);
            File.Copy(symbolPackagePath, stagedSymbolPackagePath);
            return new PushExecutionContext(stagedPackagePath, source, stagingDirectory, stagingDirectory);
        }
        catch
        {
            TryDeleteDirectory(stagingDirectory);
            throw;
        }
    }

    private static bool IsSameOrChildDirectory(string candidate, string root)
    {
        var comparison = FrameworkCompatibility.PathStringComparison();
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCandidate = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedCandidate, normalizedRoot, comparison) ||
               normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    /// <summary>
    /// Anchors explicit relative filesystem sources to the caller's NuGet configuration context before
    /// companion-symbol handling moves the process into a package or staging directory. Bare NuGet.config
    /// source names and absolute URIs retain their original meaning.
    /// </summary>
    private static string ResolvePushSource(string source, string workingDirectory)
    {
        var trimmed = source.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out _))
            return trimmed;

        var normalized = PathValueResolver.NormalizeSeparators(trimmed);
        if (Path.IsPathRooted(normalized))
            return Path.GetFullPath(normalized);

        return normalized.StartsWith(".", StringComparison.Ordinal) ||
               normalized.IndexOf(Path.DirectorySeparatorChar) >= 0
            ? PathValueResolver.Resolve(workingDirectory, normalized)
            : trimmed;
    }

    private static string ResolveWorkingDirectory(string? workingDirectory, string packagePath)
    {
        if (!string.IsNullOrWhiteSpace(workingDirectory))
            return workingDirectory!;

        var packageDirectory = Path.GetDirectoryName(packagePath);
        if (!string.IsNullOrWhiteSpace(packageDirectory))
            return packageDirectory!;

        return Environment.CurrentDirectory;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static string? FirstLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var nonEmptyValue = value!;

        return nonEmptyValue
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim();
    }

    private static string NormalizeRuntimeDirectoryRoot(string? runtimeDirectoryRoot)
        => string.IsNullOrWhiteSpace(runtimeDirectoryRoot)
            ? Path.Combine(Path.GetTempPath(), "PowerForge", "runtime", "dotnet-nuget")
            : runtimeDirectoryRoot!;

    private readonly struct PushExecutionContext
    {
        public PushExecutionContext(
            string packagePath,
            string source,
            string workingDirectory,
            string? stagingDirectory)
        {
            PackagePath = packagePath;
            Source = source;
            WorkingDirectory = workingDirectory;
            StagingDirectory = stagingDirectory;
        }

        public string PackagePath { get; }
        public string Source { get; }
        public string WorkingDirectory { get; }
        public string? StagingDirectory { get; }
    }
}
