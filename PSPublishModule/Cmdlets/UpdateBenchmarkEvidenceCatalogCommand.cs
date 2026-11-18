using System;
using System.Linq;
using System.Collections;
using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Adds one normalized benchmark result to a schema 5 evidence catalog.
/// </summary>
/// <remarks>
/// <para>
/// Each lane is keyed by comparison, platform, framework, profile and lane parameters such as processor
/// affinity. The replaced lane is kept in the catalog's superseded history with its date and hash.
/// </para>
/// <para>
/// Publishing applies the catalog's publish policy: the diagnostic profile is never published; standard and
/// full lanes need clean source provenance, the runner block, at least the policy's warmups, a comparison
/// declaration with a library policy, and per-cell sample counts, confidence intervals and passed output
/// validation. Failed cells are recorded with their reason and left out of public claims instead of failing
/// the whole lane. Call it once per comparison as results arrive so a later failure never loses earlier evidence.
/// </para>
/// </remarks>
/// <example>
/// <summary>Record a published Windows lane</summary>
/// <code>$capture = Start-BenchmarkProvenanceCapture -SourceRoot . -ArtifactRoot .\Build\BenchmarkDotNet.Artifacts -Metadata @{ 'benchmark.workload.id' = 'tabular-65k' } -RunMode full
/// dotnet run -c Release --project .\Benchmarks -- --artifacts .\Build\BenchmarkDotNet.Artifacts
/// $capture | Complete-BenchmarkProvenanceCapture
/// $result = Import-BenchmarkResult .\Build\BenchmarkDotNet.Artifacts -ValidationPath .\Build\validation.json
/// $result | Update-BenchmarkEvidenceCatalog -Path .\Website\data\benchmark-index.json -ComparisonId tabular-65k -ResultPath windows-full.json -Profile full -Framework net10.0 -DeclarationPath .\Benchmarks\benchmarks.json -Publish</code>
/// </example>
[Cmdlet(VerbsData.Update, "BenchmarkEvidenceCatalog", SupportsShouldProcess = true)]
[OutputType(typeof(BenchmarkEvidenceCatalog))]
public sealed class UpdateBenchmarkEvidenceCatalogCommand : PSCmdlet
{
    /// <summary>Normalized benchmark result, usually supplied by <c>Import-BenchmarkResult</c>.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    [ValidateNotNull]
    public BenchmarkRunResult InputObject { get; set; } = null!;

    /// <summary>Evidence catalog JSON path.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>Stable identifier shared only by equivalent workloads. It must not encode framework or affinity.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string ComparisonId { get; set; } = string.Empty;

    /// <summary>Portable path or URL to the normalized result consumed by the website.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string ResultPath { get; set; } = string.Empty;

    /// <summary>
    /// Local filesystem destination for the normalized result artifact. Required when <see cref="ResultPath"/>
    /// is root-relative (such as /data/benchmarks/x.json), absolute or a URL; the update fails without it rather
    /// than writing to a drive root. The result is always written, as the lane's current run or, when a newer run
    /// already holds the lane, as a superseded run. The file name gets the result's SHA-256 so no run is overwritten.
    /// </summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? ResultArtifactPath { get; set; }

    /// <summary>
    /// Evidence profile: diagnostic (never published), standard or full (complete BenchmarkDotNet).
    /// The older run-mode name <c>quick</c> is accepted as diagnostic.
    /// </summary>
    [Parameter(Mandatory = true)]
    [Alias("RunMode")]
    [ValidateSet("diagnostic", "standard", "full", "quick")]
    public string Profile { get; set; } = string.Empty;

    /// <summary>Target framework of the lane, such as net10.0. Resolved from metadata or the runtime when omitted.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Framework { get; set; }

    /// <summary>Additional lane parameters, such as @{ affinity = '0xffff0000' }.</summary>
    [Parameter]
    public Hashtable? LaneParameter { get; set; }

    /// <summary>Schema 5 declaration document (library policy and comparisons) that replaces the catalog's declarations.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? DeclarationPath { get; set; }

    /// <summary>Publish policy that replaces the catalog's policy. Defaults to the catalog's current policy.</summary>
    [Parameter]
    public BenchmarkPublishPolicy? Policy { get; set; }

    /// <summary>
    /// Marks this lane as suitable for public benchmark claims. The lane must meet the publish policy.
    /// </summary>
    [Parameter]
    public SwitchParameter Publish { get; set; }

    /// <summary>Platforms expected before the public comparison is complete.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string[]? ExpectedPlatform { get; set; }

    /// <summary>
    /// Producing operating-system platform for artifacts, such as BenchmarkDotNet CSV, that do not
    /// carry OS metadata. Conflicting embedded labels are rejected.
    /// </summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Platform { get; set; }

    /// <summary>Updates the catalog.</summary>
    protected override void ProcessRecord()
    {
        var catalogPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
        string profile = string.Equals(Profile, "quick", StringComparison.OrdinalIgnoreCase) ? "diagnostic" : Profile;
        if (!ShouldProcess(catalogPath, $"Record {ComparisonId}/{profile} benchmark evidence"))
            return;

        var request = new BenchmarkEvidenceUpdateRequest
        {
            Result = InputObject,
            ComparisonId = ComparisonId,
            ResultPath = ResultPath,
            ResultArtifactPath = string.IsNullOrWhiteSpace(ResultArtifactPath)
                ? null
                : SessionState.Path.GetUnresolvedProviderPathFromPSPath(ResultArtifactPath),
            Profile = BenchmarkEvidenceCatalogService.ParseProfile(profile),
            Publish = Publish.IsPresent,
            Platform = Platform,
            Framework = Framework,
            Parameters = LaneParameter?.Cast<DictionaryEntry>()
                .ToDictionary(item => item.Key?.ToString() ?? string.Empty, item => item.Value?.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            ExpectedPlatforms = ExpectedPlatform,
            Declarations = string.IsNullOrWhiteSpace(DeclarationPath)
                ? null
                : new BenchmarkDeclarationService().Read(SessionState.Path.GetUnresolvedProviderPathFromPSPath(DeclarationPath)),
            Policy = Policy
        };
        WriteObject(new BenchmarkEvidenceCatalogService().UpdateFile(catalogPath, request));
    }
}
