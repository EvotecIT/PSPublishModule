using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge.Web;

public static partial class WebLinkService
{
    /// <summary>Applies reviewed redirect and ignored-404 candidate files into committed link data.</summary>
    public static WebLinkReviewApplyResult ApplyReviewCandidates(WebLinkReviewApplyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.ApplyRedirects && !options.ApplyIgnored404)
            throw new ArgumentException("Choose at least one target: redirects or ignored404.", nameof(options));

        var result = new WebLinkReviewApplyResult
        {
            DryRun = options.DryRun
        };

        if (options.ApplyRedirects)
            result.Redirects = ApplyRedirectCandidates(options);
        if (options.ApplyIgnored404)
            result.Ignored404 = ApplyIgnored404Candidates(options);

        return result;
    }

    private static WebLinkReviewApplySection ApplyRedirectCandidates(WebLinkReviewApplyOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RedirectCandidatesPath))
            throw new ArgumentException("RedirectCandidatesPath is required when applying redirects.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.RedirectsPath))
            throw new ArgumentException("RedirectsPath is required when applying redirects.", nameof(options));

        var candidatePath = Path.GetFullPath(options.RedirectCandidatesPath);
        var targetPath = Path.GetFullPath(options.RedirectsPath);
        if (!File.Exists(candidatePath))
            throw new FileNotFoundException("Redirect candidate file was not found.", candidatePath);

        var existing = File.Exists(targetPath)
            ? ReadExistingRedirects(targetPath)
            : new List<LinkRedirectRule>();
        var candidates = ReadExistingRedirects(candidatePath);

        if (options.EnableRedirects)
        {
            foreach (var candidate in candidates)
                candidate.Enabled = true;
        }

        var merged = MergeRedirectCandidates(existing, candidates, options.ReplaceExisting, out var skipped, out var replaced);
        if (!options.DryRun)
            WriteRedirectJson(targetPath, merged);

        return new WebLinkReviewApplySection
        {
            CandidatePath = candidatePath,
            TargetPath = targetPath,
            ExistingCount = existing.Count,
            CandidateCount = candidates.Count,
            WrittenCount = merged.Count,
            SkippedDuplicateCount = skipped,
            ReplacedCount = replaced
        };
    }

    private static WebLinkReviewApplySection ApplyIgnored404Candidates(WebLinkReviewApplyOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Ignored404CandidatesPath))
            throw new ArgumentException("Ignored404CandidatesPath is required when applying ignored404.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.Ignored404Path))
            throw new ArgumentException("Ignored404Path is required when applying ignored404.", nameof(options));

        var candidatePath = Path.GetFullPath(options.Ignored404CandidatesPath);
        var targetPath = Path.GetFullPath(options.Ignored404Path);
        if (!File.Exists(candidatePath))
            throw new FileNotFoundException("Ignored-404 candidate file was not found.", candidatePath);

        var existing = File.Exists(targetPath)
            ? LoadIgnored404Rules(targetPath).ToList()
            : new List<Ignored404Rule>();
        var candidates = LoadIgnored404Rules(candidatePath).ToList();

        var merged = MergeIgnored404Rules(existing, candidates, options.ReplaceExisting, out var skipped, out var replaced);
        if (!options.DryRun)
            WriteIgnored404Json(targetPath, merged);

        return new WebLinkReviewApplySection
        {
            CandidatePath = candidatePath,
            TargetPath = targetPath,
            ExistingCount = existing.Count,
            CandidateCount = candidates.Count,
            WrittenCount = merged.Count,
            SkippedDuplicateCount = skipped,
            ReplacedCount = replaced
        };
    }
}

/// <summary>Options for applying reviewed link-service candidate files.</summary>
public sealed class WebLinkReviewApplyOptions
{
    /// <summary>Apply redirect candidates.</summary>
    public bool ApplyRedirects { get; set; }
    /// <summary>Apply ignored-404 candidates.</summary>
    public bool ApplyIgnored404 { get; set; }
    /// <summary>Candidate redirect JSON path.</summary>
    public string? RedirectCandidatesPath { get; set; }
    /// <summary>Committed redirect JSON path.</summary>
    public string? RedirectsPath { get; set; }
    /// <summary>Candidate ignored-404 JSON path.</summary>
    public string? Ignored404CandidatesPath { get; set; }
    /// <summary>Committed ignored-404 JSON path.</summary>
    public string? Ignored404Path { get; set; }
    /// <summary>Replace existing rows that have the same merge key.</summary>
    public bool ReplaceExisting { get; set; }
    /// <summary>Enable redirect candidates before writing them.</summary>
    public bool EnableRedirects { get; set; }
    /// <summary>Compute the merge result without writing target files.</summary>
    public bool DryRun { get; set; }
}

/// <summary>Result from applying reviewed link-service candidate files.</summary>
public sealed class WebLinkReviewApplyResult
{
    /// <summary>True when target files were not written.</summary>
    public bool DryRun { get; set; }
    /// <summary>Redirect merge summary.</summary>
    public WebLinkReviewApplySection? Redirects { get; set; }
    /// <summary>Ignored-404 merge summary.</summary>
    public WebLinkReviewApplySection? Ignored404 { get; set; }
}

/// <summary>Per-file merge summary for link review candidate application.</summary>
public sealed class WebLinkReviewApplySection
{
    /// <summary>Resolved candidate file path.</summary>
    public string CandidatePath { get; set; } = string.Empty;
    /// <summary>Resolved target file path.</summary>
    public string TargetPath { get; set; } = string.Empty;
    /// <summary>Rows loaded from the target before merge.</summary>
    public int ExistingCount { get; set; }
    /// <summary>Rows loaded from the candidate file.</summary>
    public int CandidateCount { get; set; }
    /// <summary>Rows in the merged output.</summary>
    public int WrittenCount { get; set; }
    /// <summary>Candidate rows skipped because an existing row used the same key.</summary>
    public int SkippedDuplicateCount { get; set; }
    /// <summary>Existing rows replaced because replacement was requested.</summary>
    public int ReplacedCount { get; set; }
}
