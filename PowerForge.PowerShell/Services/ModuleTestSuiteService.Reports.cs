using System.IO;
using System.Text;

namespace PowerForge;

public sealed partial class ModuleTestSuiteService
{
    // Save through the same managed archive/repository engine as Save-ManagedModule.
    // This avoids a PSResourceGet bootstrap just to obtain the pinned test tool.
    private void SavePinnedPester(string version, string moduleRoot, bool force)
    {
        if (!Version.TryParse(version, out var parsed) || parsed.Major < 5 || parsed.Build < 0 || parsed.Revision >= 0)
            throw new ArgumentException("PesterVersion must be an exact stable version (x.y.z), Pester 5 or newer.");

        _logger.Info($"Saving Pester {version} for the test host.");
        // Async repository continuations must not call a synchronous PowerShell host's
        // stream methods. Replay the existing buffered log on the calling thread.
        var logs = new BufferedLogger { IsVerbose = _logger.IsVerbose };
        try
        {
            new ManagedModuleInstallService(logs).InstallAsync(new ManagedModuleInstallRequest
            {
                Name = "Pester",
                Version = version,
                Repository = new ManagedModuleRepository("PSGallery", ManagedModuleCatalogDefaults.PowerShellGalleryV2),
                Scope = ManagedModuleInstallScope.Custom,
                ModuleRoot = moduleRoot,
                AcceptLicense = true,
                Force = force
            }).GetAwaiter().GetResult();
        }
        finally
        {
            BufferedLogEntry[] entries;
            lock (logs.Entries) entries = logs.Entries.ToArray();
            new BufferedLogSupportService().WriteTail(entries, _logger, maxEntries: int.MaxValue);
        }
    }

    // Query the actual install host rather than guessing Documents/OneDrive/custom module paths.
    private string ReadDependencyModulePaths()
    {
        const string marker = "PFTESTPATH::";
        var request = PowerShellRunRequest.ForCommand(
            "Write-Output ('PFTESTPATH::' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($env:PSModulePath)))",
            TimeSpan.FromMinutes(1), preferPwsh: true);
        var result = _runner.Run(request);
        var line = result.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(value => value.StartsWith(marker, StringComparison.Ordinal));
        if (result.ExitCode != 0 || line is null)
            throw new InvalidOperationException($"Cannot read dependency install host module paths. {result.StdErr}");
        return line.Substring(marker.Length);
    }

    private static string? ResolveOutputPath(string projectPath, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(projectPath, path));
    }

    private static void ValidateReportPaths(ModuleTestSuiteSpec spec)
    {
        var paths = new[] { spec.ResultsPath, spec.JUnitResultsPath, spec.SummaryPath }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => ResolveOutputPath(spec.ProjectPath, path)!).ToArray();
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
            throw new ArgumentException("NUnit, JUnit and summary output paths must be different.", nameof(spec));
    }

    private static void PrepareResultFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // A setup/discovery failure must never leave a previous run's green report.
        if (File.Exists(path)) File.Delete(path);
    }

    private static void WriteSummary(string? path, ModuleTestSuiteResult? result, string? error)
    {
        if (path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var text = new StringBuilder("## Module tests\n\n");
        if (result is null)
        {
            text.AppendLine("Test execution failed before results were available.\n");
            text.AppendLine(EscapeMarkdown(error ?? "Unknown test execution failure."));
        }
        else
        {
            text.AppendLine($"Pester {EscapeMarkdown(result.PesterVersion ?? "unknown")}: {result.PassedCount}/{result.TotalCount} passed, {result.FailedCount} failed, {result.SkippedCount} skipped. Exit code: {result.ExitCode}.\n");
            foreach (var failure in result.FailureAnalysis?.FailedTests ?? Array.Empty<ModuleTestFailureInfo>())
            {
                text.AppendLine($"- **{EscapeMarkdown(failure.Name)}**: {EscapeMarkdown(failure.ErrorMessage)}");
            }
            if (result.ExitCode != 0 && result.FailedCount == 0)
                text.AppendLine("Pester reported a discovery, setup, report-writing, or empty-suite failure. Check the test log.");
        }
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    private static string EscapeMarkdown(string text)
        => System.Net.WebUtility.HtmlEncode(text).Replace("\r", " ").Replace("\n", " ")
            .Replace("\\", "\\\\").Replace("*", "\\*").Replace("[", "\\[").Replace("]", "\\]").Replace("`", "\\`");
}
