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
        _runtimeDirectoryRoot = string.IsNullOrWhiteSpace(runtimeDirectoryRoot)
            ? Path.Combine(Path.GetTempPath(), "PowerForge", "runtime", "dotnet-nuget")
            : runtimeDirectoryRoot;
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

        try
        {
            File.WriteAllText(responseFilePath, BuildPushResponseFileContent(request));

            var processResult = await _processRunner.RunAsync(
                new ProcessRunRequest(
                    _dotNetExecutable,
                    ResolveWorkingDirectory(request.WorkingDirectory, request.PackagePath),
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
        if (string.IsNullOrWhiteSpace(request.PackagePath))
            throw new ArgumentException("PackagePath is required.", nameof(request));
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
            "sign",
            request.PackagePath,
            "--certificate-fingerprint",
            request.CertificateFingerprint,
            "--certificate-store-location",
            request.CertificateStoreLocation,
            "--certificate-store-name",
            request.CertificateStoreName,
            "--timestamper",
            request.TimeStampServer
        };

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

    private static string BuildPushResponseFileContent(DotNetNuGetPushRequest request)
    {
        var lines = new List<string> {
            "nuget",
            "push",
            QuoteResponseFileValue(request.PackagePath),
            "--api-key",
            QuoteResponseFileValue(request.ApiKey),
            "--source",
            QuoteResponseFileValue(request.Source)
        };

        if (request.SkipDuplicate)
            lines.Add("--skip-duplicate");

        return string.Join(Environment.NewLine, lines);
    }

    private static string ResolveWorkingDirectory(string? workingDirectory, string packagePath)
    {
        if (!string.IsNullOrWhiteSpace(workingDirectory))
            return workingDirectory;

        var packageDirectory = Path.GetDirectoryName(packagePath);
        if (!string.IsNullOrWhiteSpace(packageDirectory))
            return packageDirectory;

        return Environment.CurrentDirectory;
    }

    private static string QuoteResponseFileValue(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

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

    private static string? FirstLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
    }
}
