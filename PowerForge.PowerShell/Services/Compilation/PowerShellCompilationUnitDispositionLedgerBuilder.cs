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

        var methods = (emittedMethods ?? shapedCompilation?.Methods ?? Array.Empty<PowerShellCompiledMethod>()).ToArray();
        var nestedBodies = methods.SelectMany(static method => method.NestedBodies).ToArray();
        var wrappedMethodKeys = GetWrappedMethodKeys(plan.Mode, artifactKind, rootSourcePath, shapedCompilation);
        var plannedSourcePaths = plan.Files
            .Select(static file => Path.GetFullPath(file.FullPath))
            .ToHashSet(PowerShellCompilationPathSafety.PathComparer);
        var moduleRuntimeDependencies = plan.Dependencies
            .Where(IsRuntimeDependency)
            .Where(dependency => dependency.SourcePath is null ||
                                 !plannedSourcePaths.Contains(Path.GetFullPath(dependency.SourcePath)))
            .Select(FormatDependencyCause)
            .Where(static cause => !string.IsNullOrWhiteSpace(cause))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static cause => cause, StringComparer.Ordinal)
            .ToArray();

        var entries = new List<PowerShellCompilationUnitDisposition>();
        var emittedMethodOwners = new List<KeyValuePair<PowerShellCompiledMethod, PowerShellCompilationUnitDisposition>>();
        var authoredOwners = new List<(string SourcePath, FunctionDefinitionAst? Declaration, PowerShellCompilationUnitDisposition Owner)>();
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
                var declaration = FindFunctionDeclaration(file.Units, indexedUnit.Index, unit, functionExtents);
                var extent = declaration?.Extent;
                var matchingMethods = methods
                    .Where(static candidate => !candidate.IsSyntheticNoOpExecutableEntry)
                    .Where(candidate => MethodMatches(candidate, shapedCompilation, file.FullPath, unit, declaration))
                    .ToArray();
                var method = matchingMethods.FirstOrDefault(static candidate => candidate.Lifecycle?.Execution != PowerShellCompilationLifecycleExecution.HostedSteppablePipeline);
                var lifecycleMethod = matchingMethods.FirstOrDefault(static candidate => candidate.Lifecycle?.Execution == PowerShellCompilationLifecycleExecution.HostedSteppablePipeline);
                var dispositionMethod = method ?? lifecycleMethod;
                var ownedBodies = declaration is null ? Array.Empty<PowerShellCompiledBodyIdentity>() : nestedBodies.Where(body =>
                    PowerShellCompilationPathSafety.PathEquals(body.SourcePath, file.FullPath) &&
                    body.StartOffset == declaration.Extent.StartOffset && body.EndOffset == declaration.Extent.EndOffset &&
                    string.Equals(body.DeclarationName, declaration.Name, StringComparison.Ordinal)).ToArray();
                var promotedRegions = shapedCompilation?.PromotedRegions
                    .Where(candidate => RegionMatches(candidate, file.FullPath, unit, declaration))
                    .ToArray() ?? Array.Empty<PowerShellCompiledRegion>();
                var unitKey = declaration is null
                    ? PowerShellHybridModuleComposer.GetCompiledMethodKey(file.FullPath, unit.Name, unit.StartLine)
                    : PowerShellHybridModuleComposer.GetCompiledMethodKey(file.FullPath, declaration);
                var emitted = method is not null || ownedBodies.Length > 0 ||
                              plan.Mode == PowerShellCompilationMode.Strict &&
                              artifactKind == PowerShellCompilationArtifactKind.Executable &&
                              methods.Length > 0 &&
                              unit.Kind == PowerShellCompilationUnitKind.Script &&
                              unit.IsCompilable;
                var emittedBinaryCmdlet = artifactKind == PowerShellCompilationArtifactKind.BinaryModule &&
                                          dispositionMethod?.NativeFunctionBinding is null &&
                                          (wrappedMethodKeys.Contains(unitKey) || lifecycleMethod is not null);
                var retainedHostedSource = IsRetainedHostedSource(
                    plan.Mode,
                    artifactKind,
                    unit,
                    emitted,
                    wrappedMethodKeys.Contains(unitKey),
                    lifecycleMethod is not null);
                var runtimeCommandRegions = Math.Max(dispositionMethod?.RequiresPowerShellCommandRegions == true
                    ? Math.Max(1, dispositionMethod.HostedRegionSiteCount)
                    : 0, ownedBodies.Where(static body => body.RequiresPowerShellCommandRegions)
                        .Select(static body => Math.Max(1, body.HostedRegionSiteCount)).DefaultIfEmpty(0).Max());
                var moduleStateReadBoundaryCrossings = Math.Max(dispositionMethod?.RequiresPowerShellModuleStateRead == true
                    ? Math.Max(1, dispositionMethod.PowerShellModuleStateReadSiteCount)
                    : 0, ownedBodies.Where(static body => body.RequiresPowerShellModuleStateRead)
                        .Select(static body => Math.Max(1, body.PowerShellModuleStateReadSiteCount)).DefaultIfEmpty(0).Max());
                var moduleStateWriteBoundaryCrossings = Math.Max(dispositionMethod?.RequiresPowerShellModuleStateWrite == true
                    ? Math.Max(1, dispositionMethod.PowerShellModuleStateWriteSiteCount)
                    : 0, ownedBodies.Where(static body => body.RequiresPowerShellModuleStateWrite)
                        .Select(static body => Math.Max(1, body.PowerShellModuleStateWriteSiteCount)).DefaultIfEmpty(0).Max());
                var moduleStateBoundaryCrossings = moduleStateReadBoundaryCrossings + moduleStateWriteBoundaryCrossings;
                var nativeFunctionBinding = dispositionMethod?.NativeFunctionBinding is not null ||
                                            ownedBodies.Any(static body => body.NativeFunctionBinding is not null);
                var runtimeRouted = nativeFunctionBinding || retainedHostedSource || runtimeCommandRegions > 0 || moduleStateBoundaryCrossings > 0;
                var rejected = !emitted &&
                               (plan.Mode == PowerShellCompilationMode.Strict ||
                                artifactKind == PowerShellCompilationArtifactKind.Library);
                var omitted = artifactKind == PowerShellCompilationArtifactKind.Library && !emitted;
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
                    .ToList();
                if (plan.Mode == PowerShellCompilationMode.Hybrid &&
                    artifactKind == PowerShellCompilationArtifactKind.Executable &&
                    unit.Kind == PowerShellCompilationUnitKind.Script &&
                    unit.IsCompilable && retainedHostedSource && !emitted)
                {
                    diagnosticChain.Add(new PowerShellCompilationDispositionCause(
                        PowerShellCompilationDiagnosticCode.ArtifactShaping,
                        PowerShellCompilationFeatureIds.ExecutableScriptRoot,
                        "Hybrid executable script statements remain in the hosted entry point; only eligible named functions are emitted as CLR methods.",
                        unit.StartLine,
                        1));
                }

                var disposition = new PowerShellCompilationUnitDisposition(
                    PowerShellCompilationExplanationService.ComputeUnitId(relativePath, unit),
                    relativePath,
                    unit.Name,
                    unit.Kind,
                    unit.StartLine,
                    unit.IsCompilable,
                    emittedClrMethod: emitted,
                    emittedBinaryCmdlet,
                    retainedHostedSource,
                    runtimeCommandRegions,
                    boundaryCrossings: runtimeCommandRegions +
                                        moduleStateBoundaryCrossings +
                                        (nativeFunctionBinding ? 1 : 0) +
                                        (retainedHostedSource && (emitted || emittedBinaryCmdlet) ? 1 : 0) +
                                        promotedRegions.Length,
                    shapingFallback: retainedHostedSource && (unit.IsCompilable || lifecycleMethod is not null),
                    omitted,
                    rejected,
                    emitted || emittedBinaryCmdlet ? dispositionMethod?.GeneratedName ??
                        (ownedBodies.Length == 1 ? ownedBodies[0].GeneratedMemberName : string.Empty) : string.Empty,
                    dependencyCauses: runtimeRouted
                        ? GetUnitDependencyCauses(plan.Dependencies, file.FullPath, relativePath)
                        : Array.Empty<string>(),
                    boundaryCauses: GetBoundaryCauses(dispositionMethod, ownedBodies, retainedHostedSource, promotedRegions),
                    diagnosticChain,
                    moduleStateReadBoundaryCrossings,
                    moduleStateWriteBoundaryCrossings,
                    dispositionMethod?.RegionGraph ?? (ownedBodies.Length == 1 ? ownedBodies[0].RegionGraph : null) ??
                        promotedRegions.FirstOrDefault()?.RegionGraph,
                    promotedRegions.Length,
                    promotedRegions.Select(static region => region.GeneratedName).ToArray(),
                    nativeFunctionBinding);
                entries.Add(disposition);
                authoredOwners.Add((file.FullPath, declaration, disposition));
                foreach (var candidate in matchingMethods)
                    emittedMethodOwners.Add(new KeyValuePair<PowerShellCompiledMethod, PowerShellCompilationUnitDisposition>(candidate, disposition));
            }
        }

        var emittedBodyOwners = emittedMethodOwners.SelectMany(binding => binding.Key.NestedBodies.Select(body =>
            new KeyValuePair<PowerShellCompiledBodyIdentity, PowerShellCompilationUnitDisposition>(body,
                FindEmittedBodyOwner(body, authoredOwners)))).ToArray();

        return new PowerShellCompilationUnitDispositionLedger(
            entries.ToArray(),
            (deliveryRuntimeCauses ?? Array.Empty<string>())
                .Concat(moduleRuntimeDependencies)
                .Where(static cause => !string.IsNullOrWhiteSpace(cause))
                .Select(static cause => cause.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static cause => cause, StringComparer.Ordinal)
                .ToArray(),
            emittedMethodOwners,
            emittedBodyOwners);
    }

    /// <summary>Uses authored declaration offsets and containment; generated method names never choose an owner.</summary>
    private static PowerShellCompilationUnitDisposition FindEmittedBodyOwner(
        PowerShellCompiledBodyIdentity body,
        IReadOnlyList<(string SourcePath, FunctionDefinitionAst? Declaration, PowerShellCompilationUnitDisposition Owner)> authoredOwners)
    {
        if (string.IsNullOrWhiteSpace(body.SourcePath) || body.StartOffset < 0 || body.EndOffset < body.StartOffset)
            throw new InvalidOperationException("An emitted native body has no valid authored source identity.");
        var fileOwners = authoredOwners.Where(item => PowerShellCompilationPathSafety.PathEquals(item.SourcePath, body.SourcePath)).ToArray();
        if (fileOwners.Length == 0)
            throw new InvalidOperationException("An emitted native body is outside the final authored source inventory.");
        if (body.DeclarationName is not null)
        {
            var exact = fileOwners.Where(item => item.Declaration?.Extent.StartOffset == body.StartOffset).ToArray();
            if (exact.Length > 0)
            {
                if (exact.Length != 1 || exact[0].Declaration!.Extent.EndOffset != body.EndOffset ||
                    !exact[0].Declaration!.Name.Equals(body.DeclarationName, StringComparison.Ordinal) ||
                    !exact[0].Owner.Name.Equals(body.DeclarationName, StringComparison.Ordinal))
                    throw new InvalidOperationException("An emitted native declaration does not have one exact authored-unit identity.");
                return exact[0].Owner;
            }
        }
        var enclosing = fileOwners.Where(item => item.Declaration is not null &&
                item.Declaration.Extent.StartOffset <= body.StartOffset && item.Declaration.Extent.EndOffset >= body.EndOffset)
            .OrderBy(item => item.Declaration!.Extent.EndOffset - item.Declaration.Extent.StartOffset).ToArray();
        if (enclosing.Length > 0)
        {
            var width = enclosing[0].Declaration!.Extent.EndOffset - enclosing[0].Declaration.Extent.StartOffset;
            if (enclosing.Skip(1).Any(item => item.Declaration!.Extent.EndOffset - item.Declaration.Extent.StartOffset == width))
                throw new InvalidOperationException("An emitted native body has ambiguous enclosing authored-unit ownership.");
            return enclosing[0].Owner;
        }
        var scripts = fileOwners.Where(static item => item.Owner.Kind == PowerShellCompilationUnitKind.Script).ToArray();
        return scripts.Length == 1 ? scripts[0].Owner
            : throw new InvalidOperationException("An emitted native body has no unique authored script or enclosing function owner.");
    }

    private static FunctionDefinitionAst[] GetFunctionExtents(string sourcePath)
    {
        if (!File.Exists(sourcePath)) return Array.Empty<FunctionDefinitionAst>();
        return PowerShellSourceParser.GetRootFunctionDeclarations(Parser.ParseFile(sourcePath, out _, out _))
            .Select(static declaration => declaration.Syntax).ToArray();
    }

    private static FunctionDefinitionAst? FindFunctionDeclaration(
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
        bool wrapped,
        bool hostedLifecycle)
    {
        if (mode == PowerShellCompilationMode.Package) return true;
        if (mode != PowerShellCompilationMode.Hybrid || artifactKind == PowerShellCompilationArtifactKind.Library) return false;
        if (hostedLifecycle) return true;
        if (unit.Kind == PowerShellCompilationUnitKind.Script) return !emitted;
        if (artifactKind == PowerShellCompilationArtifactKind.BinaryModule) return !wrapped;
        return !emitted;
    }

    private static string[] GetUnitDependencyCauses(
        IEnumerable<PowerShellCompilationDependency> dependencies,
        string fullPath,
        string relativePath)
        => dependencies
            .Where(IsRuntimeDependency)
            .Where(dependency => dependency.SourcePath is not null &&
                                 (PowerShellCompilationPathSafety.PathEquals(dependency.SourcePath, fullPath) ||
                                  NormalizePath(dependency.RelativePath).Equals(
                                      relativePath,
                                      PowerShellCompilationPathSafety.GetPathComparison(fullPath))))
            .Select(FormatDependencyCause)
            .Where(static cause => !string.IsNullOrWhiteSpace(cause))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static cause => cause, StringComparer.Ordinal)
            .ToArray();

    private static bool IsRuntimeDependency(PowerShellCompilationDependency dependency)
        => dependency.Disposition is
            PowerShellCompilationDependencyDisposition.ExternalRequirement or
            PowerShellCompilationDependencyDisposition.Missing or
            PowerShellCompilationDependencyDisposition.PreservedScript;

    private static string FormatDependencyCause(PowerShellCompilationDependency dependency)
        => string.IsNullOrWhiteSpace(dependency.Note)
            ? dependency.RelativePath
            : dependency.RelativePath + ": " + dependency.Note;

    private static bool MethodMatches(
        PowerShellCompiledMethod method,
        PowerShellTypedCompilationResult? shapedCompilation,
        string fullPath,
        PowerShellCompilationUnitPlan unit,
        FunctionDefinitionAst? declaration)
    {
        var methodPath = string.IsNullOrWhiteSpace(method.SourcePath)
            ? shapedCompilation?.SourcePath ?? string.Empty
            : method.SourcePath;
        return PowerShellCompilationPathSafety.PathEquals(methodPath, fullPath) &&
               method.SourceName.Equals(unit.Name, StringComparison.OrdinalIgnoreCase) &&
               (unit.Kind == PowerShellCompilationUnitKind.Script || method.SourceLine == unit.StartLine) &&
               (method.ConditionalDeclarationOffset is null || method.ConditionalDeclarationOffset == declaration?.Extent.StartOffset);
    }

    private static bool RegionMatches(
        PowerShellCompiledRegion region,
        string fullPath,
        PowerShellCompilationUnitPlan unit,
        FunctionDefinitionAst? declaration)
        => unit.Kind == PowerShellCompilationUnitKind.Function &&
           declaration is not null &&
           PowerShellCompilationPathSafety.PathEquals(region.SourcePath, fullPath) &&
           region.SourceName.Equals(unit.Name, StringComparison.OrdinalIgnoreCase) &&
           region.SourceLine == unit.StartLine &&
           declaration.Body.Extent.StartOffset <= region.StartOffset &&
           declaration.Body.Extent.EndOffset >= region.EndOffset;

    private static string[] GetBoundaryCauses(
        PowerShellCompiledMethod? method,
        IReadOnlyList<PowerShellCompiledBodyIdentity> bodies,
        bool retainedHostedSource,
        IReadOnlyList<PowerShellCompiledRegion> promotedRegions)
    {
        var causes = new List<string>();
        if (method is null && bodies.Count > 0)
            causes.Add("The authored declaration is implemented by a private emitted native body.");
        if (method?.NativeFunctionBinding is not null || bodies.Any(static body => body.NativeFunctionBinding is not null))
            causes.Add("The emitted CLR body uses native PowerShell parameter binding and invocation-owned variable storage.");
        if (retainedHostedSource) causes.Add("Authored source remains on the hosted PowerShell path after artifact shaping.");
        if (method?.RequiresPowerShellCommandRegions == true || bodies.Any(static body => body.RequiresPowerShellCommandRegions))
            causes.Add("The emitted CLR method contains hosted PowerShell command regions.");
        if (method?.RequiresPowerShellRuntimeState == true || bodies.Any(static body => body.RequiresPowerShellRuntimeState))
            causes.Add("The emitted CLR method captures PowerShell runtime state.");
        if (method?.RequiresPowerShellStatementErrors == true || bodies.Any(static body => body.RequiresPowerShellStatementErrors))
            causes.Add("The emitted CLR method uses the PowerShell statement-error host for error identity and continuation.");
        if (method?.RequiresPowerShellStopping == true || bodies.Any(static body => body.RequiresPowerShellStopping))
            causes.Add("The emitted CLR method requires the invocation's native PowerShell loop-stopping callback.");
        var reads = (method?.RequiredPowerShellModuleVariables ?? Array.Empty<string>())
            .Concat(bodies.SelectMany(static body => body.RequiredPowerShellModuleVariables))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        var writes = (method?.WrittenPowerShellModuleVariables ?? Array.Empty<string>())
            .Concat(bodies.SelectMany(static body => body.WrittenPowerShellModuleVariables))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        if (reads.Length > 0)
            causes.Add("The emitted CLR method reads live parent Hybrid script-module state: " +
                       string.Join(", ", reads.Select(static name => "$script:" + name)) + ".");
        if (writes.Length > 0)
            causes.Add("The emitted CLR method writes live parent Hybrid script-module state: " +
                       string.Join(", ", writes.Select(static name => "$script:" + name)) + ".");
        if ((method?.RequiresPowerShellModuleState == true || bodies.Any(static body => body.RequiresPowerShellModuleState)) &&
            reads.Length == 0 && writes.Length == 0)
            causes.Add("The emitted CLR method depends on live parent Hybrid script-module state through a compiled local call.");
        if (method?.Lifecycle?.Execution == PowerShellCompilationLifecycleExecution.HostedSteppablePipeline) causes.Add("The emitted cmdlet uses a hosted advanced-function lifecycle.");
        if (promotedRegions.Count > 0)
            causes.Add($"The retained function delegates {promotedRegions.Count} typed region(s) to generated CLR helpers while keeping its PowerShell command surface.");
        if (promotedRegions.Any(static region => region.RequiresLocalOwnershipGuard))
            causes.Add("Promoted initialization helpers require fresh invocation-local targets; regions with existing targets execute their original PowerShell statements in place.");
        if (promotedRegions.Any(static region => region.RequiresPowerShellStopping))
            causes.Add("Promoted CLR loop helpers require the retained invocation's native PowerShell stopping callback.");
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
