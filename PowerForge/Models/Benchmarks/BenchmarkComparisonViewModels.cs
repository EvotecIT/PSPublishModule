using System;
using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Presentation-ready view of a benchmark evidence catalog: every declared comparison with every measured
/// run, each contender and each case side by side. Built by <see cref="BenchmarkComparisonViewBuilder"/>.
/// </summary>
public sealed class BenchmarkComparisonCatalogView
{
    /// <summary>Area identifiers, in catalog order.</summary>
    public string[] Areas { get; set; } = Array.Empty<string>();

    /// <summary>Every declared comparison, measured or not.</summary>
    public BenchmarkComparisonView[] Comparisons { get; set; } = Array.Empty<BenchmarkComparisonView>();
}

/// <summary>One declared comparison and its measured runs.</summary>
public sealed class BenchmarkComparisonView
{
    /// <summary>Stable comparison identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Product area identifier.</summary>
    public string Area { get; set; } = string.Empty;

    /// <summary>Human-readable title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Declared libraries. The first is the subject the comparison is written for.</summary>
    public string[] Libraries { get; set; } = Array.Empty<string>();

    /// <summary>The library the comparison is written for (the first declared library).</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>True when at least one run has been published.</summary>
    public bool Measured { get; set; }

    /// <summary>Identifier of the run to show first: full before quick, published before unpublished, newest first.</summary>
    public string PreferredRunId { get; set; } = string.Empty;

    /// <summary>Measured runs, ordered by platform and run mode.</summary>
    public BenchmarkRunView[] Runs { get; set; } = Array.Empty<BenchmarkRunView>();

    /// <summary>Contenders across all runs, with their relative time in each run, in <see cref="Runs"/> order.</summary>
    public BenchmarkContenderAcrossRunsView[] Overview { get; set; } = Array.Empty<BenchmarkContenderAcrossRunsView>();
}

/// <summary>One contender's relative time in every run of a comparison.</summary>
public sealed class BenchmarkContenderAcrossRunsView
{
    /// <summary>Contender display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>True for the comparison's subject library.</summary>
    public bool IsSubject { get; set; }

    /// <summary>One cell per run, in run order.</summary>
    public BenchmarkRatioView[] Runs { get; set; } = Array.Empty<BenchmarkRatioView>();
}

/// <summary>A ratio to the fastest contender, or an absent value.</summary>
public sealed class BenchmarkRatioView
{
    /// <summary>Geometric mean of the per-case time ratios to the fastest contender, or null when absent.</summary>
    public double? Ratio { get; set; }

    /// <summary>Display text such as "1.00×" or "2.4×"; "—" when absent.</summary>
    public string Text { get; set; } = "—";

    /// <summary>True when this contender is the fastest in the run.</summary>
    public bool IsFastest { get; set; }
}

/// <summary>One measured run of a comparison.</summary>
public sealed class BenchmarkRunView
{
    /// <summary>Identifier unique within the comparison, such as "windows-full".</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Normalized platform identifier.</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>Platform display name.</summary>
    public string PlatformLabel { get; set; } = string.Empty;

    /// <summary>Evidence profile of the lane: diagnostic, standard or full. Named RunMode for existing templates.</summary>
    public string RunMode { get; set; } = string.Empty;

    /// <summary>True for full runs.</summary>
    public bool IsFull { get; set; }

    /// <summary>True when the run is published for public claims.</summary>
    public bool Published { get; set; }

    /// <summary>Run timestamp.</summary>
    public DateTimeOffset GeneratedUtc { get; set; }

    /// <summary>Run date as yyyy-MM-dd.</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>Portable path or URL of the raw result.</summary>
    public string ResultPath { get; set; } = string.Empty;

    /// <summary>Source commit the run measured, when recorded.</summary>
    public string SourceCommit { get; set; } = string.Empty;

    /// <summary>Recorded environment.</summary>
    public BenchmarkRunEnvironmentView Environment { get; set; } = new();

    /// <summary>Dependency versions keyed by library identifier, as recorded by the run.</summary>
    public Dictionary<string, string> Versions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Statistic shown for time: median, or mean when the result has no median.</summary>
    public string Statistic { get; set; } = "median";

    /// <summary>Parameters that have the same value in every case, shown once for the workload.</summary>
    public BenchmarkParameterView[] Workload { get; set; } = Array.Empty<BenchmarkParameterView>();

    /// <summary>Contenders ordered subject first, then by relative time.</summary>
    public BenchmarkContenderView[] Contenders { get; set; } = Array.Empty<BenchmarkContenderView>();

    /// <summary>Cases with one cell per contender, in <see cref="Contenders"/> order.</summary>
    public BenchmarkCaseView[] Cases { get; set; } = Array.Empty<BenchmarkCaseView>();

    /// <summary>The subject against each other contender.</summary>
    public BenchmarkHeadToHeadView[] HeadToHead { get; set; } = Array.Empty<BenchmarkHeadToHeadView>();
}

/// <summary>Environment fields worth showing next to a run.</summary>
public sealed class BenchmarkRunEnvironmentView
{
    /// <summary>Processor name.</summary>
    public string Processor { get; set; } = string.Empty;

    /// <summary>Operating system description without build details in brackets.</summary>
    public string OperatingSystem { get; set; } = string.Empty;

    /// <summary>Runtime version without the build suffix.</summary>
    public string Runtime { get; set; } = string.Empty;

    /// <summary>Logical core count, when recorded.</summary>
    public int? LogicalCores { get; set; }

    /// <summary>Processor affinity mask the workload was pinned to, when recorded.</summary>
    public string Affinity { get; set; } = string.Empty;

    /// <summary>True when the run recorded processor, operating system or runtime.</summary>
    public bool Recorded { get; set; }

    /// <summary>One-line summary, or "Environment not recorded".</summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary>A benchmark parameter and its value.</summary>
public sealed class BenchmarkParameterView
{
    /// <summary>Parameter name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Parameter value.</summary>
    public string Value { get; set; } = string.Empty;
}

/// <summary>One contender in a run.</summary>
public sealed class BenchmarkContenderView
{
    /// <summary>Display name: the declared library, plus a variant when the suite measures several for one library.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Declared library this contender belongs to, or the raw scenario name when none matches.</summary>
    public string Library { get; set; } = string.Empty;

    /// <summary>True for the comparison's subject library.</summary>
    public bool IsSubject { get; set; }

    /// <summary>Dependency version, when the run recorded one.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Relative time over all cases the contender ran.</summary>
    public BenchmarkRatioView Relative { get; set; } = new();

    /// <summary>Number of cases where this contender was the fastest.</summary>
    public int Wins { get; set; }

    /// <summary>Number of cases this contender completed.</summary>
    public int Cases { get; set; }

    /// <summary>1-based rank by relative time.</summary>
    public int Rank { get; set; }
}

/// <summary>One measured case: a parameter combination every contender ran.</summary>
public sealed class BenchmarkCaseView
{
    /// <summary>Label built from the parameters that vary between cases; empty for a single case.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Parameters that vary between cases.</summary>
    public BenchmarkParameterView[] Parameters { get; set; } = Array.Empty<BenchmarkParameterView>();

    /// <summary>One cell per contender.</summary>
    public BenchmarkCellView[] Cells { get; set; } = Array.Empty<BenchmarkCellView>();
}

/// <summary>One contender's measurement in one case.</summary>
public sealed class BenchmarkCellView
{
    /// <summary>Contender display name.</summary>
    public string Contender { get; set; } = string.Empty;

    /// <summary>True for the subject library.</summary>
    public bool IsSubject { get; set; }

    /// <summary>True when the contender has no row for this case.</summary>
    public bool Missing { get; set; }

    /// <summary>True when the row did not succeed.</summary>
    public bool Failed { get; set; }

    /// <summary>Time per operation in milliseconds.</summary>
    public double? TimeMs { get; set; }

    /// <summary>Time display text such as "2.85 ms" or "881 µs".</summary>
    public string TimeText { get; set; } = "—";

    /// <summary>Allocated bytes per operation.</summary>
    public double? AllocatedBytes { get; set; }

    /// <summary>Allocation display text such as "1.2 MB".</summary>
    public string AllocatedText { get; set; } = string.Empty;

    /// <summary>Time relative to the fastest contender in this case.</summary>
    public BenchmarkRatioView Relative { get; set; } = new();

    /// <summary>Bar width as a percentage of the slowest contender in this case.</summary>
    public double BarPercent { get; set; }
}

/// <summary>The subject library against one other contender.</summary>
public sealed class BenchmarkHeadToHeadView
{
    /// <summary>Subject contender.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Other contender.</summary>
    public string Other { get; set; } = string.Empty;

    /// <summary>Geometric mean of subject time divided by the other's time; below 1 means the subject is faster.</summary>
    public double? TimeRatio { get; set; }

    /// <summary>Geometric mean of subject allocations divided by the other's allocations.</summary>
    public double? AllocationRatio { get; set; }

    /// <summary>Cases both contenders completed.</summary>
    public int Cases { get; set; }

    /// <summary>ahead, behind or even, from the subject's point of view (3% tolerance).</summary>
    public string Outcome { get; set; } = "even";

    /// <summary>Sentence such as "OfficeIMO is 1.43× faster than Sylvan".</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Supporting detail such as "OfficeIMO allocates 1.8× less · 1 case".</summary>
    public string Detail { get; set; } = string.Empty;
}
