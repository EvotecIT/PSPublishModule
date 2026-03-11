using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PowerForge;

internal sealed class NuGetPackagePublishService
{
    private readonly ILogger _logger;
    private readonly Func<string, string, string, bool, DotNetRepositoryReleaseService.PackagePushResult> _pushPackage;

    public NuGetPackagePublishService(
        ILogger logger,
        Func<string, string, string, bool, DotNetRepositoryReleaseService.PackagePushResult>? pushPackage = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pushPackage = pushPackage ?? PushPackage;
    }

    public NuGetPackagePublishResult Execute(NuGetPackagePublishRequest request, Func<string, bool>? shouldPublishPackage = null)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var result = new NuGetPackagePublishResult();
        var roots = (request.Roots ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (roots.Length == 0)
        {
            result.Success = false;
            result.ErrorMessage = "No paths were provided.";
            return result;
        }

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                result.Success = false;
                result.ErrorMessage = $"Path '{root}' not found.";
                return result;
            }
        }

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packages = roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.nupkg", SearchOption.AllDirectories))
            .Where(path => unique.Add(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (packages.Length == 0)
        {
            result.Success = false;
            result.ErrorMessage = $"No packages found in {string.Join(", ", roots)}";
            return result;
        }

        foreach (var package in packages)
        {
            if (shouldPublishPackage is not null && !shouldPublishPackage(package))
            {
                result.PublishedItems.Add(package);
                continue;
            }

            var pushResult = _pushPackage(package, request.ApiKey, request.Source, request.SkipDuplicate)
                ?? new DotNetRepositoryReleaseService.PackagePushResult
                {
                    Outcome = DotNetRepositoryReleaseService.PackagePushOutcome.Failed,
                    Message = "Push handler returned no result."
                };

            switch (pushResult.Outcome)
            {
                case DotNetRepositoryReleaseService.PackagePushOutcome.Published:
                case DotNetRepositoryReleaseService.PackagePushOutcome.SkippedDuplicate:
                    result.PublishedItems.Add(package);
                    break;
                default:
                    result.Success = false;
                    result.FailedItems.Add(package);
                    _logger.Verbose($"dotnet nuget push failed for {package}.");
                    if (pushResult.Message is string message && message.Length > 0)
                        _logger.Verbose(message);
                    break;
            }
        }

        return result;
    }

    private static DotNetRepositoryReleaseService.PackagePushResult PushPackage(string packagePath, string apiKey, string source, bool skipDuplicate)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

#if NET472
        var args = new List<string>
        {
            "nuget", "push", packagePath,
            "--api-key", apiKey,
            "--source", source
        };
        if (skipDuplicate)
            args.Add("--skip-duplicate");
        psi.Arguments = BuildWindowsArgumentString(args);
#else
        psi.ArgumentList.Add("nuget");
        psi.ArgumentList.Add("push");
        psi.ArgumentList.Add(packagePath);
        psi.ArgumentList.Add("--api-key");
        psi.ArgumentList.Add(apiKey);
        psi.ArgumentList.Add("--source");
        psi.ArgumentList.Add(source);
        if (skipDuplicate)
            psi.ArgumentList.Add("--skip-duplicate");
#endif

        using var process = Process.Start(psi);
        if (process is null)
        {
            return new DotNetRepositoryReleaseService.PackagePushResult
            {
                Outcome = DotNetRepositoryReleaseService.PackagePushOutcome.Failed,
                Message = "Failed to start dotnet."
            };
        }

        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return DotNetRepositoryReleaseService.ClassifyNuGetPushOutcome(process.ExitCode, skipDuplicate, stdErr, stdOut);
    }

#if NET472
    private static string BuildWindowsArgumentString(IEnumerable<string> arguments)
        => string.Join(" ", arguments.Select(EscapeWindowsArgument));

    private static string EscapeWindowsArgument(string arg)
    {
        if (arg is null)
            return "\"\"";
        if (arg.Length == 0)
            return "\"\"";

        var needsQuotes = arg.Any(ch => char.IsWhiteSpace(ch) || ch == '"');
        if (!needsQuotes)
            return arg;

        var sb = new System.Text.StringBuilder();
        sb.Append('"');

        var backslashCount = 0;
        foreach (var ch in arg)
        {
            if (ch == '\\')
            {
                backslashCount++;
                continue;
            }

            if (ch == '"')
            {
                sb.Append('\\', backslashCount * 2 + 1);
                sb.Append('"');
                backslashCount = 0;
                continue;
            }

            if (backslashCount > 0)
            {
                sb.Append('\\', backslashCount);
                backslashCount = 0;
            }

            sb.Append(ch);
        }

        if (backslashCount > 0)
            sb.Append('\\', backslashCount * 2);

        sb.Append('"');
        return sb.ToString();
    }
#endif
}
