namespace PowerForge;

public sealed partial class DotNetNuGetClient
{
    /// <summary>
    /// Signs packages one at a time so one transient certificate-provider or file-access
    /// failure cannot hide packages that were already signed by a multi-package command.
    /// Each failed package receives one immediate retry, except a timeout: a stalled
    /// certificate or timestamp operation stops the batch so the operator can act.
    /// </summary>
    internal async Task<(DotNetNuGetSignResult Result, string[] FailedPackages)> SignPackagesIndividuallyAsync(
        DotNetNuGetSignRequest request,
        CancellationToken cancellationToken = default)
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
            DotNetNuGetSignResult? last = null;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                last = await SignPackageAsync(
                    new DotNetNuGetSignRequest(
                        packagePath,
                        request.CertificateFingerprint,
                        request.CertificateStoreLocation,
                        request.TimeStampServer,
                        request.CertificateStoreName,
                        request.Overwrite || attempt > 1,
                        request.WorkingDirectory,
                        request.Timeout),
                    cancellationToken).ConfigureAwait(false);
                if (last.Succeeded)
                {
                    messages.Add($"{Path.GetFileName(packagePath)}: " +
                                 (attempt == 1 ? "signed" : "signed on retry"));
                    break;
                }
                if (last.TimedOut)
                    break;
            }

            if (last is not null && !last.Succeeded)
                failures.Add((packagePath, last));
            if (last?.TimedOut == true)
                break;
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
            : $"Signing failed for {failures.Count} of {request.PackagePaths.Length} package(s): {details}";
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
