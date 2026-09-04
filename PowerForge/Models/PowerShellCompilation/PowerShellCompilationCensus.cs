using System;

namespace PowerForge;

/// <summary>One product or source tree included in a compilation census.</summary>
public sealed class PowerShellCompilationCensusProduct
{
    /// <summary>Creates a product census result using the original public contract.</summary>
    public PowerShellCompilationCensusProduct(
        string name,
        string path,
        int sourceFiles,
        int totalUnits,
        int compilableUnits,
        int runtimeFallbackUnits,
        int parseErrorFiles,
        double analysisMilliseconds,
        PowerShellCompilationCensusBlocker[] blockers,
        PowerShellCompilationFeatureImpact[]? featureImpacts = null,
        PowerShellCompilationDependencySummary[]? dependencySummary = null,
        PowerShellCompilationResourceSummary? resourceSummary = null)
        : this(
            name,
            path,
            sourceFiles,
            totalUnits,
            compilableUnits,
            runtimeFallbackUnits,
            parseErrorFiles,
            analysisMilliseconds,
            blockers,
            featureImpacts,
            dependencySummary,
            resourceSummary,
            new PowerShellCompilationCoverageBreakdown(),
            string.Empty,
            Array.Empty<PowerShellCompilationFeatureImpact>())
    {
    }

    /// <summary>Creates a product census result using the expanded public contract.</summary>
    public PowerShellCompilationCensusProduct(
        string name,
        string path,
        int sourceFiles,
        int totalUnits,
        int compilableUnits,
        int runtimeFallbackUnits,
        int parseErrorFiles,
        double analysisMilliseconds,
        PowerShellCompilationCensusBlocker[] blockers,
        PowerShellCompilationFeatureImpact[]? featureImpacts = null,
        PowerShellCompilationDependencySummary[]? dependencySummary = null,
        PowerShellCompilationResourceSummary? resourceSummary = null,
        PowerShellCompilationCoverageBreakdown? coverage = null,
        string? sourceFingerprint = null,
        PowerShellCompilationFeatureImpact[]? functionImpacts = null)
        : this(
            name,
            path,
            sourceFiles,
            totalUnits,
            compilableUnits,
            runtimeFallbackUnits,
            parseErrorFiles,
            analysisMilliseconds,
            blockers,
            featureImpacts,
            dependencySummary,
            resourceSummary,
            coverage,
            sourceFingerprint,
            functionImpacts,
            Array.Empty<PowerShellCompilationFunctionDisposition>())
    {
    }

    /// <summary>Creates a product census result with stable per-function dispositions.</summary>
    public PowerShellCompilationCensusProduct(
        string name,
        string path,
        int sourceFiles,
        int totalUnits,
        int compilableUnits,
        int runtimeFallbackUnits,
        int parseErrorFiles,
        double analysisMilliseconds,
        PowerShellCompilationCensusBlocker[] blockers,
        PowerShellCompilationFeatureImpact[]? featureImpacts,
        PowerShellCompilationDependencySummary[]? dependencySummary,
        PowerShellCompilationResourceSummary? resourceSummary,
        PowerShellCompilationCoverageBreakdown? coverage,
        string? sourceFingerprint,
        PowerShellCompilationFeatureImpact[]? functionImpacts,
        PowerShellCompilationFunctionDisposition[]? functionDispositions)
        : this(
            name,
            path,
            sourceFiles,
            totalUnits,
            compilableUnits,
            runtimeFallbackUnits,
            parseErrorFiles,
            analysisMilliseconds,
            blockers,
            featureImpacts,
            dependencySummary,
            resourceSummary,
            coverage,
            sourceFingerprint,
            functionImpacts,
            functionDispositions,
            Array.Empty<PowerShellCompilationRegionCandidate>())
    {
    }

    /// <summary>Creates a product census result with function and typed-region dispositions.</summary>
    public PowerShellCompilationCensusProduct(
        string name,
        string path,
        int sourceFiles,
        int totalUnits,
        int compilableUnits,
        int runtimeFallbackUnits,
        int parseErrorFiles,
        double analysisMilliseconds,
        PowerShellCompilationCensusBlocker[] blockers,
        PowerShellCompilationFeatureImpact[]? featureImpacts,
        PowerShellCompilationDependencySummary[]? dependencySummary,
        PowerShellCompilationResourceSummary? resourceSummary,
        PowerShellCompilationCoverageBreakdown? coverage,
        string? sourceFingerprint,
        PowerShellCompilationFeatureImpact[]? functionImpacts,
        PowerShellCompilationFunctionDisposition[]? functionDispositions,
        PowerShellCompilationRegionCandidate[]? regionCandidates)
        : this(
            name,
            path,
            sourceFiles,
            totalUnits,
            compilableUnits,
            runtimeFallbackUnits,
            parseErrorFiles,
            analysisMilliseconds,
            blockers,
            featureImpacts,
            dependencySummary,
            resourceSummary,
            coverage,
            sourceFingerprint,
            functionImpacts,
            functionDispositions,
            regionCandidates,
            Array.Empty<PowerShellCompilationRegionOpportunity>())
    {
    }

    /// <summary>Creates a product census result with terminal candidates and analysis-only opportunities.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public PowerShellCompilationCensusProduct(
        string name,
        string path,
        int sourceFiles,
        int totalUnits,
        int compilableUnits,
        int runtimeFallbackUnits,
        int parseErrorFiles,
        double analysisMilliseconds,
        PowerShellCompilationCensusBlocker[] blockers,
        PowerShellCompilationFeatureImpact[]? featureImpacts,
        PowerShellCompilationDependencySummary[]? dependencySummary,
        PowerShellCompilationResourceSummary? resourceSummary,
        PowerShellCompilationCoverageBreakdown? coverage,
        string? sourceFingerprint,
        PowerShellCompilationFeatureImpact[]? functionImpacts,
        PowerShellCompilationFunctionDisposition[]? functionDispositions,
        PowerShellCompilationRegionCandidate[]? regionCandidates,
        PowerShellCompilationRegionOpportunity[]? regionOpportunities)
    {
        Name = name ?? string.Empty;
        Path = path ?? string.Empty;
        SourceFiles = sourceFiles;
        TotalUnits = totalUnits;
        CompilableUnits = compilableUnits;
        RuntimeFallbackUnits = runtimeFallbackUnits;
        ParseErrorFiles = parseErrorFiles;
        AnalysisMilliseconds = analysisMilliseconds;
        Blockers = blockers ?? Array.Empty<PowerShellCompilationCensusBlocker>();
        FeatureImpacts = featureImpacts ?? Array.Empty<PowerShellCompilationFeatureImpact>();
        DependencySummary = dependencySummary ?? Array.Empty<PowerShellCompilationDependencySummary>();
        ResourceSummary = resourceSummary ?? new PowerShellCompilationResourceSummary();
        Coverage = coverage ?? new PowerShellCompilationCoverageBreakdown();
        SourceFingerprint = sourceFingerprint ?? string.Empty;
        FunctionImpacts = functionImpacts ?? Array.Empty<PowerShellCompilationFeatureImpact>();
        FunctionDispositions = functionDispositions ?? Array.Empty<PowerShellCompilationFunctionDisposition>();
        RegionCandidates = regionCandidates ?? Array.Empty<PowerShellCompilationRegionCandidate>();
        RegionOpportunities = regionOpportunities ?? Array.Empty<PowerShellCompilationRegionOpportunity>();
    }

    /// <summary>Stable product name derived from the source root.</summary>
    public string Name { get; }

    /// <summary>Analyzed source root.</summary>
    public string Path { get; }

    /// <summary>Authored PowerShell source files discovered.</summary>
    public int SourceFiles { get; }

    /// <summary>Executable script and function units discovered.</summary>
    public int TotalUnits { get; }

    /// <summary>Units eligible for genuine typed compilation.</summary>
    public int CompilableUnits { get; }

    /// <summary>Units requiring PowerShell runtime fallback.</summary>
    public int RuntimeFallbackUnits { get; }

    /// <summary>Files containing parser errors.</summary>
    public int ParseErrorFiles { get; }

    /// <summary>Typed compilation coverage percentage.</summary>
    public double CompilationCoveragePercentage => TotalUnits == 0 ? 0 : CompilableUnits * 100d / TotalUnits;

    /// <summary>Elapsed analyzer time in milliseconds.</summary>
    public double AnalysisMilliseconds { get; }

    /// <summary>Aggregated typed-compilation blockers ordered by frequency.</summary>
    public PowerShellCompilationCensusBlocker[] Blockers { get; }

    /// <summary>Stable missing-feature impact measured inside this product.</summary>
    public PowerShellCompilationFeatureImpact[] FeatureImpacts { get; }

    /// <summary>Discovered runtime dependency and resource summary.</summary>
    public PowerShellCompilationDependencySummary[] DependencySummary { get; }

    /// <summary>Included, excluded, required, inferred, and unclassified resource totals.</summary>
    public PowerShellCompilationResourceSummary ResourceSummary { get; }

    /// <summary>Post-emission function coverage separated from script/module initialization.</summary>
    public PowerShellCompilationCoverageBreakdown Coverage { get; }

    /// <summary>SHA-256 identity of the discovered authored PowerShell source set.</summary>
    public string SourceFingerprint { get; }

    /// <summary>Missing-feature impact restricted to authored functions and post-emission coverage.</summary>
    public PowerShellCompilationFeatureImpact[] FunctionImpacts { get; }

    /// <summary>Stable post-shaping identity and disposition for every authored function.</summary>
    public PowerShellCompilationFunctionDisposition[] FunctionDispositions { get; }

    /// <summary>Typed CLR regions promoted inside functions that remain runtime-routed.</summary>
    public int PromotedTypedRegions => FunctionDispositions.Sum(static disposition => disposition.PromotedTypedRegions);

    /// <summary>Canonical promotion decisions for terminal regions inside retained functions.</summary>
    public PowerShellCompilationRegionCandidate[] RegionCandidates { get; internal set; }

    /// <summary>Analysis-only typed regions found inside otherwise rejected functions.</summary>
    public PowerShellCompilationRegionOpportunity[] RegionOpportunities { get; internal set; }

    /// <summary>Terminal region candidates retained after the promotion policy failed closed.</summary>
    public int RejectedTypedRegions => RegionCandidates.Count(static candidate => !candidate.Promoted);
}

/// <summary>Stable post-shaping census disposition for one authored function.</summary>
public sealed class PowerShellCompilationFunctionDisposition
{
    /// <summary>Creates one function disposition.</summary>
    public PowerShellCompilationFunctionDisposition(
        string unitId,
        string relativePath,
        string name,
        int startLine,
        bool semanticEligible,
        bool emitted,
        bool runtimeRouted,
        bool shapingFallback)
        : this(
            unitId,
            relativePath,
            name,
            startLine,
            semanticEligible,
            emitted,
            runtimeRouted,
            shapingFallback,
            promotedTypedRegions: 0)
    {
    }

    /// <summary>Creates one function disposition including promoted typed-region evidence.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public PowerShellCompilationFunctionDisposition(
        string unitId,
        string relativePath,
        string name,
        int startLine,
        bool semanticEligible,
        bool emitted,
        bool runtimeRouted,
        bool shapingFallback,
        int promotedTypedRegions)
    {
        UnitId = unitId ?? string.Empty;
        RelativePath = relativePath ?? string.Empty;
        Name = name ?? string.Empty;
        StartLine = startLine;
        SemanticEligible = semanticEligible;
        Emitted = emitted;
        RuntimeRouted = runtimeRouted;
        ShapingFallback = shapingFallback;
        PromotedTypedRegions = Math.Max(0, promotedTypedRegions);
    }

    /// <summary>Relocation-stable authored-unit identity.</summary>
    public string UnitId { get; }
    /// <summary>Portable source path relative to the assessed root.</summary>
    public string RelativePath { get; }
    /// <summary>Authored function name.</summary>
    public string Name { get; }
    /// <summary>One-based authored start line.</summary>
    public int StartLine { get; }
    /// <summary>Whether canonical semantic analysis accepted the function before shaping.</summary>
    public bool SemanticEligible { get; }
    /// <summary>Whether the delivered assembly contains a CLR implementation.</summary>
    public bool Emitted { get; }
    /// <summary>Whether the delivered function still executes PowerShell runtime semantics.</summary>
    public bool RuntimeRouted { get; }
    /// <summary>Whether artifact shaping retained a runtime path for an analyzer-eligible function.</summary>
    public bool ShapingFallback { get; }
    /// <summary>Typed CLR regions promoted while this function remains runtime-routed.</summary>
    public int PromotedTypedRegions { get; }
}

/// <summary>One aggregated blocker category in a compilation census.</summary>
public sealed class PowerShellCompilationCensusBlocker
{
    /// <summary>Creates a blocker aggregate.</summary>
    public PowerShellCompilationCensusBlocker(string code, string message, int occurrences)
    {
        Code = code ?? string.Empty;
        Message = message ?? string.Empty;
        Occurrences = occurrences;
    }

    /// <summary>Stable compiler diagnostic code.</summary>
    public string Code { get; }

    /// <summary>Representative diagnostic message.</summary>
    public string Message { get; }

    /// <summary>Number of source units or files reporting this blocker.</summary>
    public int Occurrences { get; }
}

/// <summary>Repeatable compilation coverage and analyzer-performance census.</summary>
public sealed class PowerShellCompilationCensusResult
{
    /// <summary>Creates an aggregate census result using the original public contract.</summary>
    public PowerShellCompilationCensusResult(
        string? targetFramework,
        PowerShellCompilationCensusProduct[] products,
        PowerShellCompilationCensusRegression[] regressions,
        PowerShellCompilationFeatureImpact[]? frontier = null,
        PowerShellCompilationFeaturePair[]? coBlockers = null)
        : this(
            targetFramework,
            products,
            regressions,
            frontier,
            coBlockers,
            Array.Empty<PowerShellCompilationCensusSourceDrift>(),
            Array.Empty<PowerShellCompilationFeatureImpact>(),
            Array.Empty<PowerShellCompilationFeaturePair>())
    {
    }

    /// <summary>Creates an aggregate census result.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public PowerShellCompilationCensusResult(
        string? targetFramework,
        PowerShellCompilationCensusProduct[] products,
        PowerShellCompilationCensusRegression[] regressions,
        PowerShellCompilationFeatureImpact[]? frontier = null,
        PowerShellCompilationFeaturePair[]? coBlockers = null,
        PowerShellCompilationCensusSourceDrift[]? sourceDrifts = null,
        PowerShellCompilationFeatureImpact[]? functionFrontier = null,
        PowerShellCompilationFeaturePair[]? functionCoBlockers = null)
    {
        TargetFramework = string.IsNullOrWhiteSpace(targetFramework) ? null : targetFramework;
        Products = products ?? Array.Empty<PowerShellCompilationCensusProduct>();
        Regressions = regressions ?? Array.Empty<PowerShellCompilationCensusRegression>();
        Frontier = frontier ?? Array.Empty<PowerShellCompilationFeatureImpact>();
        CoBlockers = coBlockers ?? Array.Empty<PowerShellCompilationFeaturePair>();
        SourceDrifts = sourceDrifts ?? Array.Empty<PowerShellCompilationCensusSourceDrift>();
        FunctionFrontier = functionFrontier ?? Array.Empty<PowerShellCompilationFeatureImpact>();
        FunctionCoBlockers = functionCoBlockers ?? Array.Empty<PowerShellCompilationFeaturePair>();
    }

    /// <summary>Target framework used for CLR surface analysis.</summary>
    public string? TargetFramework { get; }

    /// <summary>Per-product results.</summary>
    public PowerShellCompilationCensusProduct[] Products { get; }

    /// <summary>Regressions relative to an optional baseline.</summary>
    public PowerShellCompilationCensusRegression[] Regressions { get; }

    /// <summary>Cross-product feature priorities ordered by observed candidate impact.</summary>
    public PowerShellCompilationFeatureImpact[] Frontier { get; }

    /// <summary>Feature pairs most often observed together in the same fallback unit.</summary>
    public PowerShellCompilationFeaturePair[] CoBlockers { get; }

    /// <summary>Corpus inputs whose content no longer matches the supplied baseline.</summary>
    public PowerShellCompilationCensusSourceDrift[] SourceDrifts { get; }

    /// <summary>Cross-product feature priorities restricted to functions that could become emitted CLR methods.</summary>
    public PowerShellCompilationFeatureImpact[] FunctionFrontier { get; }

    /// <summary>Function-only feature pairs most often observed together.</summary>
    public PowerShellCompilationFeaturePair[] FunctionCoBlockers { get; }

    /// <summary>Total authored source files discovered.</summary>
    public int SourceFiles => Sum(static product => product.SourceFiles);

    /// <summary>Whether every product was evaluated through final typed artifact shaping.</summary>
    public bool PostEmissionEvaluated => Products.Length > 0 && Products.All(static product => product.Coverage.PostEmissionEvaluated);

    /// <summary>Total executable units discovered.</summary>
    public int TotalUnits => Sum(static product => product.TotalUnits);

    /// <summary>Total typed-compilation eligible units.</summary>
    public int CompilableUnits => Sum(static product => product.CompilableUnits);

    /// <summary>Total fallback units.</summary>
    public int RuntimeFallbackUnits => Sum(static product => product.RuntimeFallbackUnits);

    /// <summary>Total files containing parser errors.</summary>
    public int ParseErrorFiles => Sum(static product => product.ParseErrorFiles);

    /// <summary>Total authored function units discovered.</summary>
    public int TotalFunctions => Sum(static product => product.Coverage.TotalFunctions);

    /// <summary>Total functions emitted as typed CLR methods after artifact shaping.</summary>
    public int EmittedFunctions => Sum(static product => product.Coverage.EmittedFunctions);

    /// <summary>Total typed CLR regions promoted inside functions that remain runtime-routed.</summary>
    public int PromotedTypedRegions => Sum(static product => product.PromotedTypedRegions);

    /// <summary>Total analyzer-eligible functions lost during graph or artifact shaping.</summary>
    public int DroppedEligibleFunctions => Sum(static product => product.Coverage.DroppedEligibleFunctions);

    /// <summary>Post-emission typed-function coverage percentage.</summary>
    public double EmittedFunctionCoveragePercentage => TotalFunctions == 0 ? 0 : EmittedFunctions * 100d / TotalFunctions;

    /// <summary>Whether the current result meets or improves the supplied baseline.</summary>
    public bool Passed => Regressions.Length == 0 && SourceDrifts.Length == 0;

    private int Sum(Func<PowerShellCompilationCensusProduct, int> selector)
    {
        var value = 0;
        foreach (var product in Products) value += selector(product);
        return value;
    }
}

/// <summary>One census regression relative to a named baseline product.</summary>
public sealed class PowerShellCompilationCensusRegression
{
    /// <summary>Creates a regression.</summary>
    public PowerShellCompilationCensusRegression(string product, string metric, double baseline, double current)
    {
        Product = product ?? string.Empty;
        Metric = metric ?? string.Empty;
        Baseline = baseline;
        Current = current;
    }

    /// <summary>Product reporting the regression.</summary>
    public string Product { get; }

    /// <summary>Regressed metric.</summary>
    public string Metric { get; }

    /// <summary>Baseline metric value.</summary>
    public double Baseline { get; }

    /// <summary>Current metric value.</summary>
    public double Current { get; }
}
