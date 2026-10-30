namespace PowerForge;

public sealed partial class DotNetNuGetClient
{
    /// <summary>
    /// Signs packages one at a time and stops at the first failure so a denied or
    /// failed certificate operation cannot trigger another private-key prompt.
    /// </summary>
    internal async Task<(DotNetNuGetSignResult Result, string[] FailedPackages)> SignPackagesIndividuallyAsync(
        DotNetNuGetSignRequest request,
        CancellationToken cancellationToken = default,
        Action<string, int, DotNetNuGetSignResult?>? progress = null)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.PackagePaths.Length == 0)
            throw new ArgumentException("At least one package path is required.", nameof(request));

        var started = DateTime.UtcNow;
        var messages = new List<string>();
        var failures = new List<(string PackagePath, DotNetNuGetSignResult Result)>();
        int attemptedPackageCount = 0;
        foreach (string packagePath in request.PackagePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attemptedPackageCount++;
            progress?.Invoke(packagePath, 1, null);
            var last = await SignPackageAsync(
                    new DotNetNuGetSignRequest(
                        packagePath,
                        request.CertificateFingerprint,
                        request.CertificateStoreLocation,
                        request.TimeStampServer,
                        request.CertificateStoreName,
                        request.Overwrite,
                        request.WorkingDirectory,
                        request.Timeout),
                    cancellationToken).ConfigureAwait(false);
            progress?.Invoke(packagePath, 0, last);
            if (!last.Succeeded)
            {
                failures.Add((packagePath, last));
                break;
            }
            messages.Add($"{Path.GetFileName(packagePath)}: signed");
        }

        TimeSpan duration = DateTime.UtcNow - started;
        if (failures.Count == 0)
        {
            return (
                new DotNetNuGetSignResult(
                    0,
                    string.Join(Environment.NewLine, messages),
                    string.Empty,
                    _dotNetExecutable,
                    duration,
                    timedOut: false,
                    errorMessage: null),
                Array.Empty<string>());
        }

        string[] failedPackages = failures
            .Select(failure => failure.PackagePath)
            .Concat(request.PackagePaths.Skip(attemptedPackageCount))
            .ToArray();
        string details = string.Join(
            "; ",
            failures.Select(failure =>
                $"{Path.GetFileName(failure.PackagePath)}: " +
                (failure.Result.ErrorMessage ?? $"exit code {failure.Result.ExitCode}")));
        int unattemptedPackages = request.PackagePaths.Length - attemptedPackageCount;
        string error = failures.Any(failure => failure.Result.TimedOut)
            ? $"Signing timed out for {details}; {unattemptedPackages} remaining package(s) were not attempted. Check hardware-token authorization/PIN and the timestamp service before retrying."
            : $"Signing failed for {failures.Count} of {request.PackagePaths.Length} package(s): {details}; {unattemptedPackages} remaining package(s) were not attempted.";
        DotNetNuGetSignResult firstFailure = failures[0].Result;
        return (
            new DotNetNuGetSignResult(
                firstFailure.ExitCode == 0 ? 1 : firstFailure.ExitCode,
                string.Join(Environment.NewLine, messages),
                string.Join(
                    Environment.NewLine,
                    failures.Select(failure =>
                        $"{Path.GetFileName(failure.PackagePath)}: {failure.Result.StdErr}".TrimEnd())),
                firstFailure.Executable,
                duration,
                failures.Any(failure => failure.Result.TimedOut),
                error),
            failedPackages);
    }
}
