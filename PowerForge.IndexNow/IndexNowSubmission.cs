using System;
using System.Collections.Generic;

namespace PowerForge.IndexNow;

/// <summary>URL selection, endpoint configuration, and bounded retry settings for one submission.</summary>
public sealed class IndexNowSubmissionOptions
{
    /// <summary>Absolute public URLs to submit; fragments and duplicate URLs are removed.</summary>
    public IReadOnlyList<string> Urls { get; set; } = Array.Empty<string>();
    /// <summary>Submission endpoints; an empty list uses the official IndexNow endpoint.</summary>
    public IReadOnlyList<string> Endpoints { get; set; } = Array.Empty<string>();
    /// <summary>IndexNow ownership-verification key.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Optional absolute or host-relative location of the public key file.</summary>
    public string? KeyLocation { get; set; }
    /// <summary>Optional expected host used to report mismatched input URLs.</summary>
    public string? Host { get; set; }
    /// <summary>Whether to construct batches without sending HTTP requests.</summary>
    public bool DryRun { get; set; }
    /// <summary>Whether request failures make the aggregate result unsuccessful.</summary>
    public bool FailOnRequestError { get; set; } = true;
    /// <summary>Maximum URLs per request, capped at the protocol limit of 10,000.</summary>
    public int BatchSize { get; set; } = 500;
    /// <summary>Additional attempts after an unsuccessful request.</summary>
    public int RetryCount { get; set; } = 2;
    /// <summary>Delay in milliseconds between attempts.</summary>
    public int RetryDelayMs { get; set; } = 500;
    /// <summary>Per-attempt timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 20;
}

/// <summary>Aggregate outcome with per-request diagnostics, including dry runs.</summary>
public sealed class IndexNowSubmissionResult
{
    /// <summary>Whether this operation satisfied its configured success policy.</summary>
    public bool Success { get; set; }
    /// <summary>Whether to construct batches without sending HTTP requests.</summary>
    public bool DryRun { get; set; }
    /// <summary>Number of normalized URLs represented by this result.</summary>
    public int UrlCount { get; set; }
    /// <summary>Number of distinct destination website hosts.</summary>
    public int HostCount { get; set; }
    /// <summary>Number of host batches across all submission endpoints.</summary>
    public int RequestCount { get; set; }
    /// <summary>Number of batches that failed after retry attempts.</summary>
    public int FailedRequestCount { get; set; }
    /// <summary>Validation and request failures reported during submission.</summary>
    public string[] Errors { get; set; } = Array.Empty<string>();
    /// <summary>Nonfatal input normalization and configuration diagnostics.</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();
    /// <summary>Per-batch submission outcomes.</summary>
    public IndexNowRequestResult[] Requests { get; set; } = Array.Empty<IndexNowRequestResult>();
}

/// <summary>Outcome for one host batch sent to one endpoint.</summary>
public sealed class IndexNowRequestResult
{
    /// <summary>Submission endpoint used for this request.</summary>
    public string Endpoint { get; set; } = string.Empty;
    /// <summary>Website host represented by this batch.</summary>
    public string Host { get; set; } = string.Empty;
    /// <summary>Number of normalized URLs represented by this result.</summary>
    public int UrlCount { get; set; }
    /// <summary>Whether this operation satisfied its configured success policy.</summary>
    public bool Success { get; set; }
    /// <summary>Number of HTTP attempts; zero for a dry run.</summary>
    public int AttemptCount { get; set; }
    /// <summary>Last HTTP status code, or null when no response was received.</summary>
    public int? StatusCode { get; set; }
    /// <summary>Bounded failure diagnostic when the request did not succeed.</summary>
    public string? Error { get; set; }
    /// <summary>Bounded preview of the endpoint response.</summary>
    public string? ResponsePreview { get; set; }
}
