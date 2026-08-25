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

        var analyses = new List<AnalyzedProduct>(normalized.Length);
        foreach (var path in normalized)
            analyses.Add(AnalyzeProduct(path, targetFramework, recurse));

        var products = analyses.Select(static analysis => analysis.Product).ToArray();

        var regressions = baseline is null
            ? Array.Empty<PowerShellCompilationCensusRegression>()
            : Compare(products, baseline.Products);
        var frontier = BuildFeatureImpacts(
            analyses.SelectMany(static analysis => analysis.FeatureEvidence),
            products.Sum(static product => product.CompilableUnits),
            products.Sum(static product => product.TotalUnits),
            products);
        var coBlockers = BuildCoBlockers(analyses.SelectMany(static analysis => analysis.FeatureEvidence));
        return new PowerShellCompilationCensusResult(targetFramework, products, regressions, frontier, coBlockers);
    }

    private static AnalyzedProduct AnalyzeProduct(string path, string? targetFramework, bool recurse)
    {
        var stopwatch = Stopwatch.StartNew();
        var analyzer = new PowerShellCompilationAnalyzer();
        PowerShellCompilationPlan plan;
        PowerShellCompilationDependency[] dependencies = Array.Empty<PowerShellCompilationDependency>();
        var sourceFiles = 0;
        if (recurse)
        {
            var resolved = new PowerShellCompilationInputResolver().Resolve(path);
            dependencies = resolved.Dependencies;
            var compilationSources = resolved.CompilationSourceFiles
                .Select(Path.GetFullPath)
                .ToHashSet(PowerShellCompilationPathSafety.PathComparer);
            var analyzedCompilation = analyzer.AnalyzeFiles(
                    PowerShellCompilationMode.Analyze,
                    resolved.CompilationSourceFiles,
                    Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory(),
                    targetFramework,
                    PowerShellCompilationCapability.PowerShellStreams |
                    PowerShellCompilationCapability.LocalFunctionCalls |
                    PowerShellCompilationCapability.BoundParameters |
                    PowerShellCompilationCapability.PowerShellObjects);
            var emitted = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
                resolved.CompilationSourceFiles,
                "PowerForge.Census",
                "CompiledPowerShell",
                targetFramework);
            var exportContract = PowerShellModuleExportContract.TryRead(resolved.SourcePath);
            var exportedFunctions = exportContract?.SelectFunctions(emitted.Methods.Select(static method => method.SourceName));
            emitted = PowerShellHybridFunctionCollisionResolver.RouteNameCollisionsToFallback(emitted, targetFramework);
            emitted = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(emitted, exportedFunctions, targetFramework);
            var compiledFiles = ApplyEmittedGraphEvidence(analyzedCompilation.Files, emitted);
            var runtimeOnlyFiles = resolved.SourceFiles
                .Where(source => !compilationSources.Contains(Path.GetFullPath(source)))
                .SelectMany(source => analyzer.Analyze(new PowerShellCompilationSpec(
                    source,
                    PowerShellCompilationMode.Analyze,
                    targetFramework: targetFramework,
                    capabilities: PowerShellCompilationCapability.PowerShellStreams |
                                  PowerShellCompilationCapability.LocalFunctionCalls |
                                  PowerShellCompilationCapability.BoundParameters |
                                  PowerShellCompilationCapability.PowerShellObjects)).Files)
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
                capabilities: PowerShellCompilationCapability.PowerShellStreams |
                              PowerShellCompilationCapability.LocalFunctionCalls |
                              PowerShellCompilationCapability.BoundParameters |
                              PowerShellCompilationCapability.PowerShellObjects));
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
        var featureEvidence = CollectFeatureEvidence(path, plan).ToArray();
        var productMetrics = new[]
        {
            new ProductMetrics(path, plan.TotalUnits, plan.CompilableUnits, plan.RuntimeFallbackUnits, plan.ParseErrorFiles)
        };
        var product = new PowerShellCompilationCensusProduct(
            name,
            path,
            sourceFiles,
            plan.TotalUnits,
            plan.CompilableUnits,
            plan.RuntimeFallbackUnits,
            plan.ParseErrorFiles,
            stopwatch.Elapsed.TotalMilliseconds,
            diagnostics,
            BuildFeatureImpacts(featureEvidence, plan.CompilableUnits, plan.TotalUnits, productMetrics),
            PowerShellCompilationDependencyPlanner.Summarize(dependencies));
        return new AnalyzedProduct(product, featureEvidence);
    }

    private static IEnumerable<FeatureUnitEvidence> CollectFeatureEvidence(string product, PowerShellCompilationPlan plan)
    {
        foreach (var file in plan.Files)
        {
            foreach (var unit in file.Units)
            {
                var diagnostics = unit.Diagnostics;
                yield return new FeatureUnitEvidence(
                    product,
                    file.RelativePath + ":" + unit.StartLine + ":" + unit.Name,
                    isCompilationUnit: true,
                    diagnostics);
            }
            if (file.Diagnostics.Length > 0)
            {
                yield return new FeatureUnitEvidence(
                    product,
                    file.RelativePath,
                    isCompilationUnit: false,
                    file.Diagnostics);
            }
        }
    }

    private static PowerShellCompilationFeatureImpact[] BuildFeatureImpacts(
        IEnumerable<FeatureUnitEvidence> evidence,
        int currentCompilableUnits,
        int totalUnits,
        IEnumerable<PowerShellCompilationCensusProduct> products)
        => BuildFeatureImpacts(
            evidence,
            currentCompilableUnits,
            totalUnits,
            products.Select(static product => new ProductMetrics(
                product.Path,
                product.TotalUnits,
                product.CompilableUnits,
                product.RuntimeFallbackUnits,
                product.ParseErrorFiles)));

    private static PowerShellCompilationFeatureImpact[] BuildFeatureImpacts(
        IEnumerable<FeatureUnitEvidence> evidence,
        int currentCompilableUnits,
        int totalUnits,
        IEnumerable<ProductMetrics> products)
    {
        var units = evidence.ToArray();
        var metrics = products.ToDictionary(static product => product.Name, StringComparer.OrdinalIgnoreCase);
        return units
            .SelectMany(static unit => unit.FeatureIds)
            .Distinct(StringComparer.Ordinal)
            .Select(featureId =>
            {
                var affected = units.Where(unit => unit.FeatureIds.Contains(featureId, StringComparer.Ordinal)).ToArray();
                var affectedUnits = affected.Where(static unit => unit.IsCompilationUnit).ToArray();
                var soleBlockers = affectedUnits.Count(static unit => unit.FeatureIds.Length == 1);
                var completeProducts = affectedUnits
                    .Select(static unit => unit.Product)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(product =>
                    {
                        if (!metrics.TryGetValue(product, out var productMetrics) ||
                            productMetrics.RuntimeFallbackUnits == 0 ||
                            productMetrics.ParseErrorFiles > 0)
                            return false;
                        var fallback = units.Where(unit => unit.IsCompilationUnit && unit.Product.Equals(product, StringComparison.OrdinalIgnoreCase) && unit.FeatureIds.Length > 0).ToArray();
                        return fallback.Length == productMetrics.RuntimeFallbackUnits &&
                               fallback.All(unit => unit.FeatureIds.Length == 1 && unit.FeatureIds[0].Equals(featureId, StringComparison.Ordinal));
                    });
                var description = PowerShellCompilationFeatureCatalog.Describe(featureId);
                return new PowerShellCompilationFeatureImpact(
                    featureId,
                    description.Title,
                    description.Recommendation,
                    affected.Sum(unit => unit.Diagnostics.Count(diagnostic => diagnostic.FeatureId.Equals(featureId, StringComparison.Ordinal))),
                    affectedUnits.Length,
                    soleBlockers,
                    affected.Select(static unit => unit.Product).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    completeProducts,
                    currentCompilableUnits,
                    totalUnits);
            })
            .OrderByDescending(static impact => impact.CandidateCompleteProductsUnlocked)
            .ThenByDescending(static impact => impact.VisibleSoleBlockerUnits)
            .ThenByDescending(static impact => impact.AffectedUnits)
            .ThenByDescending(static impact => impact.Occurrences)
            .ThenBy(static impact => impact.FeatureId, StringComparer.Ordinal)
            .ToArray();
    }

    private static PowerShellCompilationFeaturePair[] BuildCoBlockers(IEnumerable<FeatureUnitEvidence> evidence)
        => evidence
            .Where(static unit => unit.IsCompilationUnit && unit.FeatureIds.Length > 1)
            .SelectMany(static unit => EnumeratePairs(unit.FeatureIds).Select(pair => new { unit.Product, unit.UnitId, pair.First, pair.Second }))
            .GroupBy(static item => new { item.First, item.Second })
            .Select(static group => new PowerShellCompilationFeaturePair(
                group.Key.First,
                group.Key.Second,
                group.Select(static item => item.Product + "\0" + item.UnitId).Distinct(StringComparer.OrdinalIgnoreCase).Count()))
            .OrderByDescending(static pair => pair.AffectedUnits)
            .ThenBy(static pair => pair.FirstFeatureId, StringComparer.Ordinal)
            .ThenBy(static pair => pair.SecondFeatureId, StringComparer.Ordinal)
            .Take(50)
            .ToArray();

    private static IEnumerable<(string First, string Second)> EnumeratePairs(IReadOnlyList<string> featureIds)
    {
        for (var first = 0; first < featureIds.Count - 1; first++)
        for (var second = first + 1; second < featureIds.Count; second++)
            yield return (featureIds[first], featureIds[second]);
    }

    private static PowerShellCompilationCensusRegression[] Compare(
        IReadOnlyList<PowerShellCompilationCensusProduct> current,
        IReadOnlyList<PowerShellCompilationCensusProduct> baseline)
    {
        var regressions = new List<PowerShellCompilationCensusRegression>();
        var unmatched = current.ToList();
        foreach (var expected in baseline)
        {
            var actual = unmatched.FirstOrDefault(candidate => PathsEqual(candidate.Path, expected.Path))
                         ?? FindUniquePortableMatch(expected, unmatched);
            if (actual is null)
            {
                regressions.Add(new PowerShellCompilationCensusRegression(expected.Name, "ProductPresent", 1, 0));
                continue;
            }
            unmatched.Remove(actual);

            AddLowerIsRegression(regressions, actual.Name, "CompilableUnits", expected.CompilableUnits, actual.CompilableUnits);
            AddLowerIsRegression(regressions, actual.Name, "SourceFiles", expected.SourceFiles, actual.SourceFiles);
            AddLowerIsRegression(regressions, actual.Name, "TotalUnits", expected.TotalUnits, actual.TotalUnits);
            AddHigherIsRegression(regressions, actual.Name, "RuntimeFallbackUnits", expected.RuntimeFallbackUnits, actual.RuntimeFallbackUnits);
            AddHigherIsRegression(regressions, actual.Name, "ParseErrorFiles", expected.ParseErrorFiles, actual.ParseErrorFiles);
        }

        return regressions.ToArray();
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return PowerShellCompilationPathSafety.PathEquals(Path.GetFullPath(left), Path.GetFullPath(right));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static PowerShellCompilationCensusProduct? FindUniquePortableMatch(
        PowerShellCompilationCensusProduct expected,
        IEnumerable<PowerShellCompilationCensusProduct> candidates)
    {
        var expectedSegments = GetPortablePathSegments(expected.Path);
        var ranked = candidates
            .Select(candidate => new { Product = candidate, Score = CountCommonSuffix(expectedSegments, GetPortablePathSegments(candidate.Path)) })
            .Where(static item => item.Score > 0)
            .ToArray();
        if (ranked.Length == 0)
            return null;
        var bestScore = ranked.Max(static item => item.Score);
        var best = ranked.Where(item => item.Score == bestScore).ToArray();
        return best.Length == 1 ? best[0].Product : null;
    }

    private static string[] GetPortablePathSegments(string path)
        => path.Replace('\\', '/').TrimEnd('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

    private static int CountCommonSuffix(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var count = 0;
        while (count < left.Count && count < right.Count &&
               left[left.Count - count - 1].Equals(right[right.Count - count - 1], StringComparison.OrdinalIgnoreCase))
            count++;
        return count;
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

    private sealed class AnalyzedProduct
    {
        internal AnalyzedProduct(PowerShellCompilationCensusProduct product, FeatureUnitEvidence[] featureEvidence)
        {
            Product = product;
            FeatureEvidence = featureEvidence;
        }

        internal PowerShellCompilationCensusProduct Product { get; }
        internal FeatureUnitEvidence[] FeatureEvidence { get; }
    }

    private sealed class FeatureUnitEvidence
    {
        internal FeatureUnitEvidence(
            string product,
            string unitId,
            bool isCompilationUnit,
            PowerShellCompilationDiagnostic[] diagnostics)
        {
            Product = product;
            UnitId = unitId;
            IsCompilationUnit = isCompilationUnit;
            Diagnostics = diagnostics;
            FeatureIds = diagnostics
                .Select(static diagnostic => diagnostic.FeatureId)
                .Where(static featureId => !string.IsNullOrWhiteSpace(featureId))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static featureId => featureId, StringComparer.Ordinal)
                .ToArray();
        }

        internal string Product { get; }
        internal string UnitId { get; }
        internal bool IsCompilationUnit { get; }
        internal PowerShellCompilationDiagnostic[] Diagnostics { get; }
        internal string[] FeatureIds { get; }
    }

    private sealed class ProductMetrics
    {
        internal ProductMetrics(string name, int totalUnits, int compilableUnits, int runtimeFallbackUnits, int parseErrorFiles)
        {
            Name = name;
            TotalUnits = totalUnits;
            CompilableUnits = compilableUnits;
            RuntimeFallbackUnits = runtimeFallbackUnits;
            ParseErrorFiles = parseErrorFiles;
        }

        internal string Name { get; }
        internal int TotalUnits { get; }
        internal int CompilableUnits { get; }
        internal int RuntimeFallbackUnits { get; }
        internal int ParseErrorFiles { get; }
    }
}
