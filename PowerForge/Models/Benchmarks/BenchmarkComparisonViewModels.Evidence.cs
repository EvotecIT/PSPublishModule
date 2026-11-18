using System;
using System.Collections.Generic;

namespace PowerForge;

/// <summary>Whether a comparison can be published, and why not.</summary>
public sealed class BenchmarkEligibilityView
{
    /// <summary>True when the comparison can appear in public claims.</summary>
    public bool Publishable { get; set; }

    /// <summary>Why it cannot be published; empty when it can.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Published coverage of one platform and framework for a comparison.</summary>
public sealed class BenchmarkAvailabilityView
{
    /// <summary>Normalized platform identifier.</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>Platform display name.</summary>
    public string PlatformLabel { get; set; } = string.Empty;

    /// <summary>Target framework, or empty.</summary>
    public string Framework { get; set; } = string.Empty;

    /// <summary>True when a published run exists.</summary>
    public bool Available { get; set; }

    /// <summary>Published profiles, such as full or standard.</summary>
    public string[] Profiles { get; set; } = Array.Empty<string>();

    /// <summary>Date of the newest published run as yyyy-MM-dd, or empty.</summary>
    public string LatestDate { get; set; } = string.Empty;

    /// <summary>Display text such as "Windows · net10.0 · full · 2026-11-18" or "Windows · net10.0 · not measured".</summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary>One declared library of a comparison and whether its results can be shown.</summary>
public sealed class BenchmarkLibraryStateView
{
    /// <summary>Library identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Restriction class: open, publish-restricted, acceptance-required, excluded or unclassified.</summary>
    public string Restriction { get; set; } = string.Empty;

    /// <summary>True when the library's results can be published.</summary>
    public bool Published { get; set; }

    /// <summary>Display note, such as "not published (licence)"; empty for published libraries.</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>Reason recorded in the library policy.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Pinned package version from the library policy.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Licence expression from the library policy.</summary>
    public string License { get; set; } = string.Empty;
}

/// <summary>How the runner measured a run, with a display line.</summary>
public sealed class BenchmarkRunnerView
{
    /// <summary>Runner kind: benchmarkdotnet, rotated, powershell-suite or process.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Runner version.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Reported statistic: median or mean.</summary>
    public string Statistic { get; set; } = string.Empty;

    /// <summary>Warmup iterations per cell.</summary>
    public int? Warmups { get; set; }

    /// <summary>Measured iterations per cell.</summary>
    public int? Iterations { get; set; }

    /// <summary>Process launches per cell.</summary>
    public int? Launches { get; set; }

    /// <summary>Work ordering.</summary>
    public string Ordering { get; set; } = string.Empty;

    /// <summary>True when cells ran inside the runner process.</summary>
    public bool? InProcess { get; set; }

    /// <summary>Processor affinity mask.</summary>
    public string Affinity { get; set; } = string.Empty;

    /// <summary>Process priority.</summary>
    public string Priority { get; set; } = string.Empty;

    /// <summary>Display line such as "BenchmarkDotNet 0.15.8 · median · 6 warmups · 15 iterations · 1 launch · sequential".</summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary>A library whose cells a run does not show, and why.</summary>
public sealed class BenchmarkNotPublishedView
{
    /// <summary>Library identifier.</summary>
    public string Library { get; set; } = string.Empty;

    /// <summary>True when the reason is the library's licence class.</summary>
    public bool Licence { get; set; }

    /// <summary>Display text, such as "not published (licence)" or the policy reason.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Number of cells left out.</summary>
    public int Cells { get; set; }
}

/// <summary>Index of the comparisons in one area, for loading the area's full data on demand.</summary>
public sealed class BenchmarkAreaIndexView
{
    /// <summary>Area identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>File name of the area's full data, relative to the index.</summary>
    public string File { get; set; } = string.Empty;

    /// <summary>Groups within the area, in declaration order.</summary>
    public string[] Groups { get; set; } = Array.Empty<string>();

    /// <summary>Number of comparisons with at least one run.</summary>
    public int Measured { get; set; }

    /// <summary>Comparison summaries.</summary>
    public BenchmarkComparisonSummaryView[] Comparisons { get; set; } = Array.Empty<BenchmarkComparisonSummaryView>();
}

/// <summary>Short description of one comparison for an index.</summary>
public sealed class BenchmarkComparisonSummaryView
{
    /// <summary>Comparison identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Group within the area.</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>Subject library.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Declared libraries.</summary>
    public string[] Libraries { get; set; } = Array.Empty<string>();

    /// <summary>True when the comparison has runs.</summary>
    public bool Measured { get; set; }

    /// <summary>Eligibility.</summary>
    public BenchmarkEligibilityView Eligibility { get; set; } = new();

    /// <summary>Head-to-head text of the preferred run, such as "OfficeIMO is 2.0× faster than Sep".</summary>
    public string[] Headlines { get; set; } = Array.Empty<string>();
}

/// <summary>Index written beside the per-area files: every area with its comparison summaries.</summary>
public sealed class BenchmarkComparisonIndexView
{
    /// <summary>Areas in catalog order.</summary>
    public BenchmarkAreaIndexView[] Areas { get; set; } = Array.Empty<BenchmarkAreaIndexView>();
}

/// <summary>Full data of one area, written as its own file.</summary>
public sealed class BenchmarkAreaView
{
    /// <summary>Area identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Every comparison in the area.</summary>
    public BenchmarkComparisonView[] Comparisons { get; set; } = Array.Empty<BenchmarkComparisonView>();
}
