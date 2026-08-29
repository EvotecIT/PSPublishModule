using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Creates the single final unit-disposition authority after all emitter and artifact shaping.</summary>
internal static class PowerShellCompilationUnitDispositionLedgerBuilder
{
    internal static PowerShellCompilationUnitDispositionLedger Create(
        PowerShellCompilationPlan plan,
        PowerShellCompilationArtifactKind artifactKind,
        PowerShellTypedCompilationResult? shapedCompilation,
        string rootSourcePath,
        IEnumerable<string>? deliveryRuntimeCauses = null,
        IEnumerable<PowerShellCompiledMethod>? emittedMethods = null)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (!Enum.IsDefined(typeof(PowerShellCompilationArtifactKind), artifactKind))
            throw new ArgumentOutOfRangeException(nameof(artifactKind));

        var methods = (emittedMethods ?? shapedCompilation?.Methods ?? Array.Empty<PowerShellCompiledMethod>())
            .Where(static method => method.Lifecycle is null)
            .ToArray();
        var wrappedMethodKeys = GetWrappedMethodKeys(plan.Mode, artifactKind, rootSourcePath, shapedCompilation);
        var runtimeDependencies = plan.Dependencies
            .Where(static dependency => dependency.Disposition is
                PowerShellCompilationDependencyDisposition.ExternalRequirement or
                PowerShellCompilationDependencyDisposition.Missing or
                PowerShellCompilationDependencyDisposition.PreservedScript)
            .Select(static dependency => string.IsNullOrWhiteSpace(dependency.Note)
                ? dependency.RelativePath
                : dependency.RelativePath + ": " + dependency.Note)
            .Where(static cause => !string.IsNullOrWhiteSpace(cause))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static cause => cause, StringComparer.Ordinal)
            .ToArray();

        var entries = new List<PowerShellCompilationUnitDisposition>();
        foreach (var file in plan.Files.OrderBy(static file => NormalizePath(file.RelativePath), StringComparer.Ordinal))
        {
            var relativePath = NormalizeRelativePath(file.RelativePath, Path.GetFileName(file.FullPath));
            var functionExtents = GetFunctionExtents(file.FullPath);
            foreach (var indexedUnit in file.Units
                         .Select(static (unit, index) => (Unit: unit, Index: index))
                         .OrderBy(static item => item.Unit.StartLine)
                         .ThenBy(static item => item.Unit.Kind)
                         .ThenBy(static item => item.Unit.Name, StringComparer.Ordinal))
            {
                var unit = indexedUnit.Unit;
                var method = methods.FirstOrDefault(candidate => MethodMatches(candidate, shapedCompilation, file.FullPath, unit));
                var unitKey = PowerShellHybridModuleComposer.GetCompiledMethodKey(file.FullPath, unit.Name, unit.StartLine);
                var emitted = method is not null;
                var retainedHostedSource = IsRetainedHostedSource(
                    plan.Mode,
                    artifactKind,
                    unit,
                    emitted,
                    wrappedMethodKeys.Contains(unitKey));
                var runtimeCommandRegions = method?.RequiresPowerShellCommandRegions == true
                    ? Math.Max(1, method.HostedRegionSiteCount)
                    : 0;
                var runtimeRouted = retainedHostedSource || runtimeCommandRegions > 0;
                var rejected = !emitted &&
                               (plan.Mode == PowerShellCompilationMode.Strict ||
                                artifactKind == PowerShellCompilationArtifactKind.Library);
                var omitted = artifactKind == PowerShellCompilationArtifactKind.Library && !emitted;
                var extent = FindFunctionExtent(file.Units, indexedUnit.Index, unit, functionExtents);
                var shapedDiagnostics = shapedCompilation?.Diagnostics.Where(diagnostic =>
                        PowerShellCompilationPathSafety.PathEquals(diagnostic.FilePath, file.FullPath) &&
                        (extent is null
                            ? diagnostic.Line == unit.StartLine
                            : IsWithinExtent(diagnostic.Line, diagnostic.Column, extent)))
                    ?? Enumerable.Empty<PowerShellCompilationDiagnostic>();
                var diagnosticChain = unit.Diagnostics.Concat(shapedDiagnostics)
                    .GroupBy(static diagnostic => diagnostic.Code + "\0" + diagnostic.FeatureId + "\0" + diagnostic.Line + "\0" + diagnostic.Column + "\0" + diagnostic.Message, StringComparer.Ordinal)
                    .Select(static group => group.First())
                    .OrderBy(static diagnostic => diagnostic.Line)
                    .ThenBy(static diagnostic => diagnostic.Column)
                    .ThenBy(static diagnostic => diagnostic.Code)
                    .ThenBy(static diagnostic => diagnostic.FeatureId, StringComparer.Ordinal)
                    .Select(static diagnostic => new PowerShellCompilationDispositionCause(
                        diagnostic.Code,
                        diagnostic.FeatureId,
                        diagnostic.Message,
                        diagnostic.Line,
                        diagnostic.Column))
                    .ToArray();

                entries.Add(new PowerShellCompilationUnitDisposition(
                    PowerShellCompilationExplanationService.ComputeUnitId(relativePath, unit),
                    relativePath,
                    unit.Name,
                    unit.Kind,
                    unit.StartLine,
                    unit.IsCompilable,
                    emittedClrMethod: emitted,
                    emittedBinaryCmdlet: artifactKind == PowerShellCompilationArtifactKind.BinaryModule && wrappedMethodKeys.Contains(unitKey),
                    retainedHostedSource,
                    runtimeCommandRegions,
                    boundaryCrossings: (method?.HostedRegionSiteCount ?? 0) + (retainedHostedSource && emitted ? 1 : 0),
                    shapingFallback: unit.IsCompilable && runtimeRouted,
                    omitted,
                    rejected,
                    method?.GeneratedName ?? string.Empty,
                    dependencyCauses: runtimeRouted ? runtimeDependencies : Array.Empty<string>(),
                    boundaryCauses: GetBoundaryCauses(method, retainedHostedSource),
                    diagnosticChain));
            }
        }

        return new PowerShellCompilationUnitDispositionLedger(
            entries.ToArray(),
            (deliveryRuntimeCauses ?? Array.Empty<string>())
                .Where(static cause => !string.IsNullOrWhiteSpace(cause))
                .Select(static cause => cause.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static cause => cause, StringComparer.Ordinal)
                .ToArray());
    }

    private static FunctionDefinitionAst[] GetFunctionExtents(string sourcePath)
    {
        if (!File.Exists(sourcePath)) return Array.Empty<FunctionDefinitionAst>();
        return Parser.ParseFile(sourcePath, out _, out _)
            .FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: true)
            .OfType<FunctionDefinitionAst>()
            .ToArray();
    }

    private static IScriptExtent? FindFunctionExtent(
        IReadOnlyList<PowerShellCompilationUnitPlan> units,
        int unitIndex,
        PowerShellCompilationUnitPlan unit,
        IReadOnlyList<FunctionDefinitionAst> functions)
    {
        if (unit.Kind != PowerShellCompilationUnitKind.Function) return null;
        var occurrence = units.Take(unitIndex).Count(candidate =>
            candidate.Kind == PowerShellCompilationUnitKind.Function &&
            candidate.StartLine == unit.StartLine &&
            candidate.Name.Equals(unit.Name, StringComparison.OrdinalIgnoreCase));
        return functions
            .Where(function => function.Body.Extent.StartLineNumber == unit.StartLine &&
                               function.Name.Equals(unit.Name, StringComparison.OrdinalIgnoreCase))
            .Skip(occurrence)
            .Select(static function => function.Extent)
            .FirstOrDefault();
    }

    private static bool IsWithinExtent(int line, int column, IScriptExtent extent)
    {
        if (line < extent.StartLineNumber || line > extent.EndLineNumber) return false;
        if (line == extent.StartLineNumber && column < extent.StartColumnNumber) return false;
        return line != extent.EndLineNumber || column <= extent.EndColumnNumber;
    }

    private static HashSet<string> GetWrappedMethodKeys(
        PowerShellCompilationMode mode,
        PowerShellCompilationArtifactKind artifactKind,
        string rootSourcePath,
        PowerShellTypedCompilationResult? shapedCompilation)
    {
        if (shapedCompilation is null || mode is PowerShellCompilationMode.Package or PowerShellCompilationMode.Analyze)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (artifactKind == PowerShellCompilationArtifactKind.BinaryModule && mode == PowerShellCompilationMode.Hybrid)
            return PowerShellHybridModuleComposer.GetWrappedCompiledMethodKeys(rootSourcePath, shapedCompilation);
        return PowerShellHybridModuleComposer.GetExecutableCompiledMethodKeys(shapedCompilation);
    }

    private static bool IsRetainedHostedSource(
        PowerShellCompilationMode mode,
        PowerShellCompilationArtifactKind artifactKind,
        PowerShellCompilationUnitPlan unit,
        bool emitted,
        bool wrapped)
    {
        if (mode == PowerShellCompilationMode.Package) return true;
        if (mode != PowerShellCompilationMode.Hybrid || artifactKind == PowerShellCompilationArtifactKind.Library) return false;
        if (unit.Kind == PowerShellCompilationUnitKind.Script) return true;
        if (artifactKind == PowerShellCompilationArtifactKind.BinaryModule) return !wrapped;
        return !emitted;
    }

    private static bool MethodMatches(
        PowerShellCompiledMethod method,
        PowerShellTypedCompilationResult? shapedCompilation,
        string fullPath,
        PowerShellCompilationUnitPlan unit)
    {
        var methodPath = string.IsNullOrWhiteSpace(method.SourcePath)
            ? shapedCompilation?.SourcePath ?? string.Empty
            : method.SourcePath;
        return unit.Kind == PowerShellCompilationUnitKind.Function &&
               PowerShellCompilationPathSafety.PathEquals(methodPath, fullPath) &&
               method.SourceName.Equals(unit.Name, StringComparison.OrdinalIgnoreCase) &&
               method.SourceLine == unit.StartLine;
    }

    private static string[] GetBoundaryCauses(PowerShellCompiledMethod? method, bool retainedHostedSource)
    {
        var causes = new List<string>();
        if (retainedHostedSource) causes.Add("Authored source remains on the hosted PowerShell path after artifact shaping.");
        if (method?.RequiresPowerShellCommandRegions == true) causes.Add("The emitted CLR method contains hosted PowerShell command regions.");
        if (method?.RequiresPowerShellRuntimeState == true) causes.Add("The emitted CLR method captures PowerShell runtime state.");
        if (method?.Lifecycle is not null) causes.Add("The emitted cmdlet uses a hosted advanced-function lifecycle.");
        return causes.ToArray();
    }

    private static string NormalizeRelativePath(string path, string fallback)
    {
        var normalized = NormalizePath(path);
        return Path.IsPathRooted(normalized) || string.IsNullOrWhiteSpace(normalized)
            ? NormalizePath(fallback)
            : normalized;
    }

    private static string NormalizePath(string path) => (path ?? string.Empty).Replace('\\', '/');
}
