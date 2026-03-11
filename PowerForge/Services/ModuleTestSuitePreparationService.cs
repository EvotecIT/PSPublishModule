using System.IO;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleTestSuitePreparationService
{
    public ModuleTestSuitePreparedContext Prepare(ModuleTestSuitePreparationRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.CurrentPath))
            throw new ArgumentException("Current path is required.", nameof(request));

        var outputFormat = request.CICD
            ? ModuleTestSuiteOutputFormat.Minimal
            : request.OutputFormat;
        var projectRoot = string.IsNullOrWhiteSpace(request.ProjectPath)
            ? request.CurrentPath
            : Path.GetFullPath(request.ProjectPath!.Trim().Trim('"'));

        if (!Directory.Exists(projectRoot))
            throw new DirectoryNotFoundException($"Path '{projectRoot}' does not exist or is not a directory");

        return new ModuleTestSuitePreparedContext
        {
            ProjectRoot = projectRoot,
            PassThru = request.PassThru || request.CICD,
            ExitOnFailure = request.ExitOnFailure || request.CICD,
            Spec = new ModuleTestSuiteSpec
            {
                ProjectPath = projectRoot,
                TestPath = request.TestPath,
                AdditionalModules = NormalizeStrings(request.AdditionalModules, "Pester", "PSWriteColor"),
                SkipModules = NormalizeStrings(request.SkipModules),
                OutputFormat = outputFormat,
                EnableCodeCoverage = request.EnableCodeCoverage,
                Force = request.Force,
                SkipDependencies = request.SkipDependencies,
                SkipImport = request.SkipImport,
                KeepResultsXml = false,
                PreferPwsh = true,
                TimeoutSeconds = request.TimeoutSeconds
            }
        };
    }

    private static string[] NormalizeStrings(string[]? values, params string[] fallback)
    {
        var source = values is { Length: > 0 } ? values : fallback;
        return source
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
