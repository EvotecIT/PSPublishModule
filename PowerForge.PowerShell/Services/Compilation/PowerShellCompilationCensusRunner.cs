using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>Runs repeatable compilation coverage censuses across product source trees.</summary>
public sealed partial class PowerShellCompilationCensusRunner
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
        var sourceDrifts = baseline is null
            ? Array.Empty<PowerShellCompilationCensusSourceDrift>()
            : CompareSourceDrifts(products, baseline.Products);
        var frontier = BuildFeatureImpacts(
            analyses.SelectMany(static analysis => analysis.FeatureEvidence),
            products.Sum(static product => product.CompilableUnits),
            products.Sum(static product => product.TotalUnits),
            products);
        var coBlockers = BuildCoBlockers(analyses.SelectMany(static analysis => analysis.FeatureEvidence));
        var functionFrontier = BuildFeatureImpacts(
            analyses.SelectMany(static analysis => analysis.FeatureEvidence),
            products.Sum(static product => product.Coverage.EmittedFunctions),
            products.Sum(static product => product.Coverage.TotalFunctions),
            products.Select(static product => new ProductMetrics(
                product.Path,
                product.Coverage.TotalFunctions,
                product.Coverage.EmittedFunctions,
                product.Coverage.FallbackFunctions,
                product.ParseErrorFiles)),
            PowerShellCompilationUnitKind.Function);
        var functionCoBlockers = BuildCoBlockers(
            analyses.SelectMany(static analysis => analysis.FeatureEvidence),
            PowerShellCompilationUnitKind.Function);
        return new PowerShellCompilationCensusResult(
            targetFramework,
            products,
            regressions,
            frontier,
            coBlockers,
            sourceDrifts,
            functionFrontier,
            functionCoBlockers);
    }

    private static AnalyzedProduct AnalyzeProduct(string path, string? targetFramework, bool recurse)
    {
        var stopwatch = Stopwatch.StartNew();
        var analyzer = new PowerShellCompilationAnalyzer();
        PowerShellCompilationPlan plan;
        PowerShellCompilationDependency[] dependencies = Array.Empty<PowerShellCompilationDependency>();
        var sourceFiles = 0;
        var sourceFingerprint = string.Empty;
        var coverage = new PowerShellCompilationCoverageBreakdown();
        PowerShellCompilationUnitDispositionLedger? dispositionLedger = null;
        PowerShellCompilationRegionCandidate[] regionCandidates = Array.Empty<PowerShellCompilationRegionCandidate>();
        if (recurse)
        {
            var resolved = new PowerShellCompilationInputResolver().Resolve(
                path,
                mode: PowerShellCompilationMode.Hybrid,
                allowDynamicModuleRuntimeSources: true);
            var analysisCapabilities = PowerShellCompilationBuildSpec.GetCapabilities(
                resolved.Kind,
                PowerShellCompilationMode.Hybrid);
            dependencies = resolved.Dependencies;
            var compilationSources = resolved.CompilationSourceFiles
                .Select(Path.GetFullPath)
                .ToHashSet(PowerShellCompilationPathSafety.PathComparer);
            var analyzedCompilation = analyzer.AnalyzeFiles(
                    PowerShellCompilationMode.Analyze,
                    resolved.CompilationSourceFiles,
                    Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory(),
                    targetFramework,
                    analysisCapabilities);
            var emitted = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
                resolved.CompilationSourceFiles,
                "PowerForge.Census",
                "CompiledPowerShell",
                targetFramework,
                analysisCapabilities);
            var exportContract = PowerShellModuleExportContract.TryRead(resolved.SourcePath);
            var exportedFunctions = exportContract?.SelectFunctions(emitted.Methods.Select(static method => method.SourceName));
            emitted = PowerShellHybridFunctionCollisionResolver.RouteNameCollisionsToFallback(
                emitted, targetFramework, capabilities: analysisCapabilities);
            emitted = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(
                emitted, exportedFunctions, targetFramework, capabilities: analysisCapabilities);
            var runtimeOnlyFiles = resolved.SourceFiles
                .Where(source => !compilationSources.Contains(Path.GetFullPath(source)))
                .SelectMany(source => analyzer.Analyze(new PowerShellCompilationSpec(
                    source,
                    PowerShellCompilationMode.Analyze,
                    targetFramework: targetFramework,
                    capabilities: analysisCapabilities)).Files)
                .Select(MarkRuntimeOnly)
                .ToArray();
            var files = analyzedCompilation.Files.Concat(runtimeOnlyFiles).ToArray();
            plan = new PowerShellCompilationPlan(
                PowerShellCompilationMode.Hybrid,
                files,
                targetFramework,
                dependencies);
            dispositionLedger = PowerShellCompilationUnitDispositionLedgerBuilder.Create(
                plan,
                PowerShellCompilationArtifactKind.BinaryModule,
                emitted,
                resolved.SourcePath);
            sourceFiles = resolved.SourceFiles.Length;
            sourceFingerprint = ComputeSourceFingerprint(resolved.SourceFiles, path);
            coverage = BuildCoverageBreakdown(dispositionLedger);
            regionCandidates = emitted.RegionCandidates;
        }
        else
        {
            var analysisCapabilities = PowerShellCompilationBuildSpec.GetCapabilities(
                PowerShellCompilationInputResolver.InferDefaultArtifactKind(path),
                PowerShellCompilationMode.Hybrid);
            plan = analyzer.Analyze(new PowerShellCompilationSpec(
                path,
                PowerShellCompilationMode.Analyze,
                recurse: false,
                targetFramework: targetFramework,
                capabilities: analysisCapabilities));
            sourceFiles = plan.Files.Length;
            sourceFingerprint = ComputeSourceFingerprint(plan.Files.Select(static file => file.FullPath), path);
            var units = plan.Files.SelectMany(static file => file.Units).ToArray();
            coverage = new PowerShellCompilationCoverageBreakdown(
                postEmissionEvaluated: false,
                totalFunctions: units.Count(static unit => unit.Kind == PowerShellCompilationUnitKind.Function),
                analyzerEligibleFunctions: units.Count(static unit => unit.Kind == PowerShellCompilationUnitKind.Function && unit.IsCompilable),
                fallbackFunctions: units.Count(static unit => unit.Kind == PowerShellCompilationUnitKind.Function && !unit.IsCompilable),
                totalScriptUnits: units.Count(static unit => unit.Kind == PowerShellCompilationUnitKind.Script),
                structurallyEligibleScriptUnits: units.Count(static unit => unit.Kind == PowerShellCompilationUnitKind.Script && unit.IsCompilable),
                fallbackScriptUnits: units.Count(static unit => unit.Kind == PowerShellCompilationUnitKind.Script && !unit.IsCompilable));
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
        var featureEvidence = CollectFeatureEvidence(path, plan, dispositionLedger).ToArray();
        var productMetrics = new[]
        {
            new ProductMetrics(
                path,
                dispositionLedger?.AnalyzedUnits ?? plan.TotalUnits,
                dispositionLedger?.EmittedUnits ?? plan.CompilableUnits,
                dispositionLedger?.RuntimeRoutedUnits ?? plan.RuntimeFallbackUnits,
                plan.ParseErrorFiles)
        };
        var functionMetrics = new[]
        {
            new ProductMetrics(
                path,
                coverage.TotalFunctions,
                coverage.EmittedFunctions,
                coverage.FallbackFunctions,
                plan.ParseErrorFiles)
        };
        var product = new PowerShellCompilationCensusProduct(
            name,
            path,
            sourceFiles,
            dispositionLedger?.AnalyzedUnits ?? plan.TotalUnits,
            dispositionLedger?.EmittedUnits ?? plan.CompilableUnits,
            dispositionLedger?.RuntimeRoutedUnits ?? plan.RuntimeFallbackUnits,
            plan.ParseErrorFiles,
            stopwatch.Elapsed.TotalMilliseconds,
            diagnostics,
            BuildFeatureImpacts(
                featureEvidence,
                dispositionLedger?.EmittedUnits ?? plan.CompilableUnits,
                dispositionLedger?.AnalyzedUnits ?? plan.TotalUnits,
                productMetrics),
            PowerShellCompilationDependencyPlanner.Summarize(dependencies),
            PowerShellCompilationResourceSummary.Create(dependencies),
            coverage,
            sourceFingerprint,
            BuildFeatureImpacts(
                featureEvidence,
                coverage.EmittedFunctions,
                coverage.TotalFunctions,
                functionMetrics,
                PowerShellCompilationUnitKind.Function),
            BuildFunctionDispositions(dispositionLedger));
        product.RegionCandidates = regionCandidates;
        return new AnalyzedProduct(product, featureEvidence);
    }

    private static IEnumerable<FeatureUnitEvidence> CollectFeatureEvidence(
        string product,
        PowerShellCompilationPlan plan,
        PowerShellCompilationUnitDispositionLedger? ledger)
    {
        var dispositions = ledger?.Entries.ToDictionary(static entry => entry.UnitId, StringComparer.Ordinal)
                           ?? new Dictionary<string, PowerShellCompilationUnitDisposition>(StringComparer.Ordinal);
        foreach (var file in plan.Files)
        {
            foreach (var unit in file.Units)
            {
                var relativePath = file.RelativePath.Replace('\\', '/');
                var unitId = PowerShellCompilationExplanationService.ComputeUnitId(relativePath, unit);
                var diagnostics = dispositions.TryGetValue(unitId, out var disposition)
                    ? disposition.DiagnosticChain.Select(cause => new PowerShellCompilationDiagnostic(
                        cause.Code,
                        cause.Message,
                        relativePath,
                        cause.Line,
                        cause.Column,
                        cause.FeatureId)).ToArray()
                    : unit.Diagnostics;
                yield return new FeatureUnitEvidence(
                    product,
                    unitId,
                    isCompilationUnit: true,
                    unit.Kind,
                    diagnostics);
            }
            if (file.Diagnostics.Length > 0)
            {
                yield return new FeatureUnitEvidence(
                    product,
                    file.RelativePath,
                    isCompilationUnit: false,
                    unitKind: null,
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
        IEnumerable<ProductMetrics> products,
        PowerShellCompilationUnitKind? unitKind = null)
    {
        var units = evidence
            .Where(unit => !unitKind.HasValue || unit.UnitKind == unitKind.Value)
            .ToArray();
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

    private static PowerShellCompilationFeaturePair[] BuildCoBlockers(
        IEnumerable<FeatureUnitEvidence> evidence,
        PowerShellCompilationUnitKind? unitKind = null)
        => evidence
            .Where(unit => unit.IsCompilationUnit &&
                           unit.FeatureIds.Length > 1 &&
                           (!unitKind.HasValue || unit.UnitKind == unitKind.Value))
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
            if (expected.Coverage.PostEmissionEvaluated)
            {
                if (!actual.Coverage.PostEmissionEvaluated)
                    regressions.Add(new PowerShellCompilationCensusRegression(actual.Name, "PostEmissionEvaluated", 1, 0));
                AddLowerIsRegression(regressions, actual.Name, "EmittedFunctions", expected.Coverage.EmittedFunctions, actual.Coverage.EmittedFunctions);
                AddHigherIsRegression(regressions, actual.Name, "FallbackFunctions", expected.Coverage.FallbackFunctions, actual.Coverage.FallbackFunctions);
                AddFunctionDispositionRegressions(
                    regressions,
                    actual.Name,
                    expected.Coverage.TotalFunctions,
                    expected.FunctionDispositions,
                    actual.Coverage.TotalFunctions,
                    actual.FunctionDispositions);
            }
        }

        return regressions.ToArray();
    }

    private static PowerShellCompilationCensusSourceDrift[] CompareSourceDrifts(
        IReadOnlyList<PowerShellCompilationCensusProduct> current,
        IReadOnlyList<PowerShellCompilationCensusProduct> baseline)
    {
        var drifts = new List<PowerShellCompilationCensusSourceDrift>();
        var unmatched = current.ToList();
        foreach (var expected in baseline)
        {
            var actual = unmatched.FirstOrDefault(candidate => PathsEqual(candidate.Path, expected.Path))
                         ?? FindUniquePortableMatch(expected, unmatched);
            if (actual is null)
                continue;
            unmatched.Remove(actual);
            if (string.IsNullOrWhiteSpace(expected.SourceFingerprint) ||
                string.IsNullOrWhiteSpace(actual.SourceFingerprint) ||
                expected.SourceFingerprint.Equals(actual.SourceFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            drifts.Add(new PowerShellCompilationCensusSourceDrift(
                actual.Name,
                expected.SourceFingerprint,
                actual.SourceFingerprint));
        }
        return drifts.ToArray();
    }

    private static PowerShellCompilationCoverageBreakdown BuildCoverageBreakdown(
        PowerShellCompilationUnitDispositionLedger ledger)
    {
        var entries = ledger.Entries;
        var totalFunctions = entries.Count(static entry => entry.Kind == PowerShellCompilationUnitKind.Function);
        var analyzerEligibleFunctions = entries.Count(static entry =>
            entry.Kind == PowerShellCompilationUnitKind.Function && entry.SemanticEligible);
        var emittedFunctions = entries.Count(static entry =>
            entry.Kind == PowerShellCompilationUnitKind.Function && entry.Emitted);
        var totalScriptUnits = entries.Count(static entry => entry.Kind == PowerShellCompilationUnitKind.Script);
        var structurallyEligibleScriptUnits = entries.Count(static entry =>
            entry.Kind == PowerShellCompilationUnitKind.Script && entry.SemanticEligible);
        return new PowerShellCompilationCoverageBreakdown(
            postEmissionEvaluated: true,
            totalFunctions,
            analyzerEligibleFunctions,
            emittedFunctions,
            entries.Count(static entry => entry.Kind == PowerShellCompilationUnitKind.Function && entry.SemanticEligible && !entry.Emitted),
            entries.Count(static entry => entry.Kind == PowerShellCompilationUnitKind.Function && entry.RuntimeRouted),
            totalScriptUnits,
            structurallyEligibleScriptUnits,
            entries.Count(static entry => entry.Kind == PowerShellCompilationUnitKind.Script && entry.RuntimeRouted),
            entries.Count(static entry => entry.Kind == PowerShellCompilationUnitKind.Function &&
                entry.DiagnosticChain.Any(static cause => cause.Code == PowerShellCompilationDiagnosticCode.RuntimeScope)),
            entries.Count(static entry => entry.Kind == PowerShellCompilationUnitKind.Script &&
                entry.DiagnosticChain.Any(static cause => cause.Code == PowerShellCompilationDiagnosticCode.RuntimeScope)));
    }

    private static PowerShellCompilationFunctionDisposition[] BuildFunctionDispositions(
        PowerShellCompilationUnitDispositionLedger? ledger)
        => ledger?.Entries
               .Where(static entry => entry.Kind == PowerShellCompilationUnitKind.Function)
               .Select(static entry => new PowerShellCompilationFunctionDisposition(
                   entry.UnitId,
                   entry.RelativePath,
                   entry.Name,
                   entry.StartLine,
                   entry.SemanticEligible,
                   entry.Emitted,
                   entry.RuntimeRouted,
                   entry.ShapingFallback,
                   entry.PromotedTypedRegions))
               .ToArray()
           ?? Array.Empty<PowerShellCompilationFunctionDisposition>();

    private static string ComputeSourceFingerprint(IEnumerable<string> files, string sourceRoot)
    {
        var fullRoot = Directory.Exists(sourceRoot)
            ? Path.GetFullPath(sourceRoot)
            : Path.GetDirectoryName(Path.GetFullPath(sourceRoot)) ?? Directory.GetCurrentDirectory();
        var identities = files
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Distinct(PowerShellCompilationPathSafety.PathComparer)
            .Select(file => new
            {
                Path = file,
                RelativePath = FrameworkCompatibility.GetRelativePath(fullRoot, file).Replace('\\', '/')
            })
            .OrderBy(static item => item.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var canonical = new StringBuilder();
        using (var fileHasher = SHA256.Create())
        {
            foreach (var identity in identities)
            {
                var contentHash = fileHasher.ComputeHash(ReadNormalizedSourceBytes(identity.Path));
                canonical.Append(identity.RelativePath)
                    .Append('\0')
                    .Append(ToLowerHex(contentHash))
                    .Append('\n');
            }
        }

        using (var aggregateHasher = SHA256.Create())
            return ToLowerHex(aggregateHasher.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static string ToLowerHex(byte[] bytes)
        => BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();

    private static byte[] ReadNormalizedSourceBytes(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var encoding = GetStrictSourceEncoding(bytes, out var offset);
        var byteWiseLineEndingsAreSafe = !HasPrefix(bytes, 0x00, 0x00, 0xFE, 0xFF) &&
                                         !HasPrefix(bytes, 0xFF, 0xFE, 0x00, 0x00) &&
                                         !HasPrefix(bytes, 0xFE, 0xFF) &&
                                         !HasPrefix(bytes, 0xFF, 0xFE);
        if (encoding is not null)
        {
            try
            {
                var text = encoding.GetString(bytes, offset, bytes.Length - offset)
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n');
                return Encoding.UTF8.GetBytes(text);
            }
            catch (DecoderFallbackException)
            {
                // A raw 0x0D/0x0A byte is an ASCII newline in UTF-8 and legacy
                // single-byte input, but may be part of a UTF-16/32 code unit.
                if (!byteWiseLineEndingsAreSafe) return bytes;
            }
        }

        var normalized = new List<byte>(bytes.Length);
        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] != (byte)'\r')
            {
                normalized.Add(bytes[index]);
                continue;
            }

            normalized.Add((byte)'\n');
            if (index + 1 < bytes.Length && bytes[index + 1] == (byte)'\n') index++;
        }
        return normalized.ToArray();
    }

    private static Encoding? GetStrictSourceEncoding(byte[] bytes, out int offset)
    {
        offset = 0;
        if (HasPrefix(bytes, 0x00, 0x00, 0xFE, 0xFF))
        {
            offset = 4;
            return new UTF32Encoding(bigEndian: true, byteOrderMark: false, throwOnInvalidCharacters: true);
        }
        if (HasPrefix(bytes, 0xFF, 0xFE, 0x00, 0x00))
        {
            offset = 4;
            return new UTF32Encoding(bigEndian: false, byteOrderMark: false, throwOnInvalidCharacters: true);
        }
        if (HasPrefix(bytes, 0xEF, 0xBB, 0xBF))
        {
            offset = 3;
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        }
        if (HasPrefix(bytes, 0xFE, 0xFF))
        {
            offset = 2;
            return new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);
        }
        if (HasPrefix(bytes, 0xFF, 0xFE))
        {
            offset = 2;
            return new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
        }
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    }

    private static bool HasPrefix(byte[] bytes, params byte[] prefix)
    {
        if (bytes.Length < prefix.Length) return false;
        for (var index = 0; index < prefix.Length; index++)
        {
            if (bytes[index] != prefix[index]) return false;
        }
        return true;
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

    internal static StringComparer CreateMethodIdentityComparer(string sourcePath)
        => PowerShellCompilationPathSafety.GetPathComparison(sourcePath) == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

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
            PowerShellCompilationUnitKind? unitKind,
            PowerShellCompilationDiagnostic[] diagnostics)
        {
            Product = product;
            UnitId = unitId;
            IsCompilationUnit = isCompilationUnit;
            UnitKind = unitKind;
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
        internal PowerShellCompilationUnitKind? UnitKind { get; }
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
