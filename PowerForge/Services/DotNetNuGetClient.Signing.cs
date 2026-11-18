namespace PowerForge;

public sealed partial class DotNetNuGetClient
{
    /// <summary>
    /// Signs packages with one <c>dotnet nuget sign</c> process each. The first package is
    /// signed alone so a hardware token is unlocked once; the rest run up to
    /// <paramref name="maxParallelism"/> at a time. The first failure stops new packages from
    /// starting, so a denied or failed certificate operation is not repeated.
    /// </summary>
    internal Task<(DotNetNuGetSignResult Result, string[] FailedPackages)> SignPackagesIndividuallyAsync(
        DotNetNuGetSignRequest request,
        CancellationToken cancellationToken = default,
        Action<string, int, DotNetNuGetSignResult?>? progress = null,
        int maxParallelism = 1)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.PackagePaths.Length == 0)
            throw new ArgumentException("At least one package path is required.", nameof(request));

        var started = DateTime.UtcNow;
        var packages = request.PackagePaths;
        var results = new DotNetNuGetSignResult?[packages.Length];
        var schedule = SigningScheduler.Run(
            packages.Length,
            maxParallelism,
            index =>
            {
                var last = SignPackageAsync(
                        new DotNetNuGetSignRequest(
                            packages[index],
                            request.CertificateFingerprint,
                            request.CertificateStoreLocation,
                            request.TimeStampServer,
                            request.CertificateStoreName,
                            request.Overwrite,
                            request.WorkingDirectory,
                            request.Timeout),
                        cancellationToken)
                    .GetAwaiter()
                    .GetResult();
                results[index] = last;
                return last.Succeeded;
            },
            started: index => progress?.Invoke(packages[index], 1, null),
            finished: (index, _, error) =>
            {
                if (error is null)
                    progress?.Invoke(packages[index], 0, results[index]);
            },
            cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var messages = Enumerable.Range(0, packages.Length)
            .Where(index => results[index]?.Succeeded == true)
            .Select(index => $"{Path.GetFileName(packages[index])}: signed")
            .ToList();
        var failures = schedule.Failures
            .Select(failure => (
                PackagePath: packages[failure.Index],
                Result: results[failure.Index] ?? new DotNetNuGetSignResult(
                    1,
                    string.Empty,
                    failure.Error?.Message ?? string.Empty,
                    _dotNetExecutable,
                    TimeSpan.Zero,
                    timedOut: false,
                    errorMessage: failure.Error?.Message ?? "dotnet nuget sign failed.")))
            .ToList();

        TimeSpan duration = DateTime.UtcNow - started;
        if (failures.Count == 0)
        {
            return Task.FromResult((
                new DotNetNuGetSignResult(
                    0,
                    string.Join(Environment.NewLine, messages),
                    string.Empty,
                    _dotNetExecutable,
                    duration,
                    timedOut: false,
                    errorMessage: null),
                Array.Empty<string>()));
        }

        string[] failedPackages = failures
            .Select(failure => failure.PackagePath)
            .Concat(packages.Where((_, index) => !schedule.Attempted[index]))
            .ToArray();
        string details = string.Join(
            "; ",
            failures.Select(failure =>
                $"{Path.GetFileName(failure.PackagePath)}: " +
                (failure.Result.ErrorMessage ?? $"exit code {failure.Result.ExitCode}")));
        int unattemptedPackages = schedule.Attempted.Count(attempted => !attempted);
        string error = failures.Any(failure => failure.Result.TimedOut)
            ? $"Signing timed out for {details}; {unattemptedPackages} remaining package(s) were not attempted. Check hardware-token authorization/PIN and the timestamp service before retrying."
            : $"Signing failed for {failures.Count} of {packages.Length} package(s): {details}; {unattemptedPackages} remaining package(s) were not attempted.";
        DotNetNuGetSignResult firstFailure = failures[0].Result;
        return Task.FromResult((
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
            failedPackages));
    }
}
