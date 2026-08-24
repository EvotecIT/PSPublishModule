using System.Diagnostics;

namespace PowerForge;

/// <summary>Runs repeatable compilation coverage censuses across product source trees.</summary>
public sealed class PowerShellCompilationCensusRunner
{
    /// <summary>Analyzes all source roots and optionally compares them with a prior census.</summary>
    public PowerShellCompilationCensusResult Run(
        IEnumerable<string> paths,
        string? targetFramework = null,
        PowerShellCompilationCensusResult? baseline = null,
        bool recurse = true)
    {
        if (paths is null) throw new ArgumentNullException(nameof(paths));
        var normalized = paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => Path.GetFullPath(path.Trim().Trim('"')))
            .Distinct(PowerShellCompilationPathSafety.PathComparer)
            .ToArray();
        if (normalized.Length == 0)
            throw new ArgumentException("At least one PowerShell product or source path is required.", nameof(paths));
        if (baseline is not null && !string.Equals(
                NormalizeTargetFramework(targetFramework),
                NormalizeTargetFramework(baseline.TargetFramework),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Compilation census baseline target framework '{baseline.TargetFramework ?? "<host>"}' does not match requested target framework '{targetFramework ?? "<host>"}'.",
                nameof(baseline));
        }

        var products = new List<PowerShellCompilationCensusProduct>(normalized.Length);
        foreach (var path in normalized)
            products.Add(AnalyzeProduct(path, targetFramework, recurse));

        var regressions = baseline is null
            ? Array.Empty<PowerShellCompilationCensusRegression>()
            : Compare(products, baseline.Products);
        return new PowerShellCompilationCensusResult(targetFramework, products.ToArray(), regressions);
    }

    private static PowerShellCompilationCensusProduct AnalyzeProduct(string path, string? targetFramework, bool recurse)
    {
        var stopwatch = Stopwatch.StartNew();
        var analyzer = new PowerShellCompilationAnalyzer();
        PowerShellCompilationPlan plan;
        var sourceFiles = 0;
        if (recurse)
        {
            var resolved = new PowerShellCompilationInputResolver().Resolve(path);
            var compilationSources = resolved.CompilationSourceFiles
                .Select(Path.GetFullPath)
                .ToHashSet(PowerShellCompilationPathSafety.PathComparer);
            var analyzedCompilation = analyzer.AnalyzeFiles(
                    PowerShellCompilationMode.Analyze,
                    resolved.CompilationSourceFiles,
                    Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory(),
                    targetFramework,
                    PowerShellCompilationCapability.PowerShellStreams | PowerShellCompilationCapability.LocalFunctionCalls);
            var emitted = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
                resolved.CompilationSourceFiles,
                "PowerForge.Census",
                "CompiledPowerShell",
                targetFramework);
            var compiledFiles = ApplyEmittedGraphEvidence(analyzedCompilation.Files, emitted);
            var runtimeOnlyFiles = resolved.SourceFiles
                .Where(source => !compilationSources.Contains(Path.GetFullPath(source)))
                .SelectMany(source => analyzer.Analyze(new PowerShellCompilationSpec(
                    source,
                    PowerShellCompilationMode.Analyze,
                    targetFramework: targetFramework,
                    capabilities: PowerShellCompilationCapability.PowerShellStreams | PowerShellCompilationCapability.LocalFunctionCalls)).Files)
                .Select(MarkRuntimeOnly)
                .ToArray();
            var files = compiledFiles.Concat(runtimeOnlyFiles).ToArray();
            plan = new PowerShellCompilationPlan(PowerShellCompilationMode.Analyze, files, targetFramework);
            sourceFiles = resolved.SourceFiles.Length;
        }
        else
        {
            plan = analyzer.Analyze(new PowerShellCompilationSpec(
                path,
                PowerShellCompilationMode.Analyze,
                recurse: false,
                targetFramework: targetFramework,
                capabilities: PowerShellCompilationCapability.PowerShellStreams | PowerShellCompilationCapability.LocalFunctionCalls));
            sourceFiles = plan.Files.Length;
        }
        stopwatch.Stop();

        var diagnostics = plan.Files
            .SelectMany(static file => file.Diagnostics.Concat(file.Units.SelectMany(static unit => unit.Diagnostics)))
            .GroupBy(static diagnostic => new { diagnostic.Code, diagnostic.Message })
            .Select(static group => new PowerShellCompilationCensusBlocker(
                group.Key.Code.ToString(),
                group.Key.Message,
                group.Count()))
            .OrderByDescending(static blocker => blocker.Occurrences)
            .ThenBy(static blocker => blocker.Code, StringComparer.Ordinal)
            .ThenBy(static blocker => blocker.Message, StringComparer.Ordinal)
            .Take(25)
            .ToArray();
        var name = Directory.Exists(path)
            ? new DirectoryInfo(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Name
            : Path.GetFileNameWithoutExtension(path);
        return new PowerShellCompilationCensusProduct(
            name,
            path,
            sourceFiles,
            plan.TotalUnits,
            plan.CompilableUnits,
            plan.RuntimeFallbackUnits,
            plan.ParseErrorFiles,
            stopwatch.Elapsed.TotalMilliseconds,
            diagnostics);
    }

    private static PowerShellCompilationCensusRegression[] Compare(
        IReadOnlyList<PowerShellCompilationCensusProduct> current,
        IReadOnlyList<PowerShellCompilationCensusProduct> baseline)
    {
        var regressions = new List<PowerShellCompilationCensusRegression>();
        var currentByPath = current.ToDictionary(
            static product => Path.GetFullPath(product.Path),
            PowerShellCompilationPathSafety.PathComparer);
        foreach (var expected in baseline)
        {
            if (!currentByPath.TryGetValue(Path.GetFullPath(expected.Path), out var actual))
            {
                regressions.Add(new PowerShellCompilationCensusRegression(expected.Name, "ProductPresent", 1, 0));
                continue;
            }

            AddLowerIsRegression(regressions, actual.Name, "CompilableUnits", expected.CompilableUnits, actual.CompilableUnits);
            AddLowerIsRegression(regressions, actual.Name, "SourceFiles", expected.SourceFiles, actual.SourceFiles);
            AddLowerIsRegression(regressions, actual.Name, "TotalUnits", expected.TotalUnits, actual.TotalUnits);
            AddHigherIsRegression(regressions, actual.Name, "RuntimeFallbackUnits", expected.RuntimeFallbackUnits, actual.RuntimeFallbackUnits);
            AddHigherIsRegression(regressions, actual.Name, "ParseErrorFiles", expected.ParseErrorFiles, actual.ParseErrorFiles);
        }

        return regressions.ToArray();
    }

    private static void AddLowerIsRegression(
        ICollection<PowerShellCompilationCensusRegression> regressions,
        string product,
        string metric,
        double baseline,
        double current)
    {
        if (current < baseline)
            regressions.Add(new PowerShellCompilationCensusRegression(product, metric, baseline, current));
    }

    private static void AddHigherIsRegression(
        ICollection<PowerShellCompilationCensusRegression> regressions,
        string product,
        string metric,
        double baseline,
        double current)
    {
        if (current > baseline)
            regressions.Add(new PowerShellCompilationCensusRegression(product, metric, baseline, current));
    }

    private static PowerShellCompilationFilePlan MarkRuntimeOnly(PowerShellCompilationFilePlan file)
    {
        var units = file.Units.Select(unit => new PowerShellCompilationUnitPlan(
            unit.Name,
            unit.Kind,
            unit.StartLine,
            unit.ReturnType,
            unit.Parameters,
            unit.Diagnostics.Concat(new[]
            {
                new PowerShellCompilationDiagnostic(
                    PowerShellCompilationDiagnosticCode.RuntimeScope,
                    "This source is loaded through a manifest runtime hook and remains on the PowerShell fallback path.",
                    file.FullPath,
                    unit.StartLine,
                    1)
            }).ToArray())).ToArray();
        return new PowerShellCompilationFilePlan(file.FullPath, file.RelativePath, units, file.Diagnostics);
    }

    private static PowerShellCompilationFilePlan[] ApplyEmittedGraphEvidence(
        IEnumerable<PowerShellCompilationFilePlan> files,
        PowerShellTypedCompilationResult emitted)
    {
        var methods = emitted.Methods
            .Select(static method => Path.GetFullPath(method.SourcePath) + "\0" + method.SourceName)
            .ToHashSet(PowerShellCompilationPathSafety.PathComparer);
        return files.Select(file =>
        {
            var fullPath = Path.GetFullPath(file.FullPath);
            var units = file.Units.Select(unit =>
            {
                if (unit.Kind != PowerShellCompilationUnitKind.Function || !unit.IsCompilable || methods.Contains(fullPath + "\0" + unit.Name))
                    return unit;
                return new PowerShellCompilationUnitPlan(
                    unit.Name,
                    unit.Kind,
                    unit.StartLine,
                    unit.ReturnType,
                    unit.Parameters,
                    unit.Diagnostics.Concat(new[]
                    {
                        new PowerShellCompilationDiagnostic(
                            PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                            "This function did not survive conservative typed function-graph emission and remains on the PowerShell fallback path.",
                            file.FullPath,
                            unit.StartLine,
                            1)
                    }).ToArray());
            }).ToArray();
            return new PowerShellCompilationFilePlan(file.FullPath, file.RelativePath, units, file.Diagnostics);
        }).ToArray();
    }

    private static string? NormalizeTargetFramework(string? targetFramework)
        => string.IsNullOrWhiteSpace(targetFramework) ? null : targetFramework!.Trim();
}
