using System;

namespace PowerForge;

/// <summary>One product or source tree included in a compilation census.</summary>
public sealed class PowerShellCompilationCensusProduct
{
    /// <summary>Creates a product census result.</summary>
    public PowerShellCompilationCensusProduct(
        string name,
        string path,
        int sourceFiles,
        int totalUnits,
        int compilableUnits,
        int runtimeFallbackUnits,
        int parseErrorFiles,
        double analysisMilliseconds,
        PowerShellCompilationCensusBlocker[] blockers)
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
    /// <summary>Creates an aggregate census result.</summary>
    public PowerShellCompilationCensusResult(
        string? targetFramework,
        PowerShellCompilationCensusProduct[] products,
        PowerShellCompilationCensusRegression[] regressions)
    {
        TargetFramework = string.IsNullOrWhiteSpace(targetFramework) ? null : targetFramework;
        Products = products ?? Array.Empty<PowerShellCompilationCensusProduct>();
        Regressions = regressions ?? Array.Empty<PowerShellCompilationCensusRegression>();
    }

    /// <summary>Target framework used for CLR surface analysis.</summary>
    public string? TargetFramework { get; }

    /// <summary>Per-product results.</summary>
    public PowerShellCompilationCensusProduct[] Products { get; }

    /// <summary>Regressions relative to an optional baseline.</summary>
    public PowerShellCompilationCensusRegression[] Regressions { get; }

    /// <summary>Total authored source files discovered.</summary>
    public int SourceFiles => Sum(static product => product.SourceFiles);

    /// <summary>Total executable units discovered.</summary>
    public int TotalUnits => Sum(static product => product.TotalUnits);

    /// <summary>Total typed-compilation eligible units.</summary>
    public int CompilableUnits => Sum(static product => product.CompilableUnits);

    /// <summary>Total fallback units.</summary>
    public int RuntimeFallbackUnits => Sum(static product => product.RuntimeFallbackUnits);

    /// <summary>Total files containing parser errors.</summary>
    public int ParseErrorFiles => Sum(static product => product.ParseErrorFiles);

    /// <summary>Whether the current result meets or improves the supplied baseline.</summary>
    public bool Passed => Regressions.Length == 0;

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
