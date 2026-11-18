namespace PowerForge;

/// <summary>Request to record one benchmark result as an evidence lane.</summary>
public sealed class BenchmarkEvidenceUpdateRequest
{
    /// <summary>Normalized benchmark result.</summary>
    public BenchmarkRunResult Result { get; set; } = null!;

    /// <summary>Stable comparison identifier. It never encodes framework or affinity.</summary>
    public string ComparisonId { get; set; } = string.Empty;

    /// <summary>Portable path or URL to the normalized run result.</summary>
    public string ResultPath { get; set; } = string.Empty;

    /// <summary>Optional local destination for the normalized result artifact.</summary>
    public string? ResultArtifactPath { get; set; }

    /// <summary>Evidence profile.</summary>
    public BenchmarkEvidenceProfile Profile { get; set; } = BenchmarkEvidenceProfile.Diagnostic;

    /// <summary>Whether the lane is intended for public claims.</summary>
    public bool Publish { get; set; }

    /// <summary>Producing platform override for artifacts that do not carry OS metadata.</summary>
    public string? Platform { get; set; }

    /// <summary>Target framework; resolved from metadata or the runtime when omitted.</summary>
    public string? Framework { get; set; }

    /// <summary>Additional lane parameters, such as <c>affinity</c>.</summary>
    public IDictionary<string, string>? Parameters { get; set; }

    /// <summary>Platforms expected for complete evidence.</summary>
    public string[]? ExpectedPlatforms { get; set; }

    /// <summary>Library policy and comparison declarations that replace the catalog's current declarations.</summary>
    public BenchmarkDeclarationDocument? Declarations { get; set; }

    /// <summary>Publish policy that replaces the catalog's current policy.</summary>
    public BenchmarkPublishPolicy? Policy { get; set; }
}

public sealed partial class BenchmarkEvidenceCatalogService
{
    /// <summary>Reads a schema 5 catalog and rejects any other schema with a clear error.</summary>
    /// <param name="path">Catalog path.</param>
    /// <returns>Catalog.</returns>
    public BenchmarkEvidenceCatalog ReadCatalog(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Catalog path is required.", nameof(path));
        using (var document = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(path)))
        {
            int schema = BenchmarkJson.TryGetPropertyIgnoreCase(document.RootElement, "schemaVersion", out var version) &&
                         version.ValueKind == System.Text.Json.JsonValueKind.Number
                ? version.GetInt32()
                : 0;
            if (schema != CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Benchmark evidence catalog '{path}' uses schema {schema}. Only schema {CurrentSchemaVersion} is read; " +
                    "older catalogs are not converted. Record fresh runs into a new schema 5 catalog.");
            }
        }
        return BenchmarkJson.Read<BenchmarkEvidenceCatalog>(path);
    }

    /// <summary>Parses a profile name. Only diagnostic, standard and full are accepted.</summary>
    /// <param name="profile">Profile name.</param>
    /// <returns>Profile.</returns>
    public static BenchmarkEvidenceProfile ParseProfile(string profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
            throw new ArgumentException("Evidence profile is required: diagnostic, standard or full.", nameof(profile));
        if (!BenchmarkKebabCaseEnumConverter<BenchmarkEvidenceProfile>.TryParse(profile, out BenchmarkEvidenceProfile value))
            throw new ArgumentException($"Unknown evidence profile '{profile}'. Use diagnostic, standard or full.", nameof(profile));
        return value;
    }

    private static string FormatProfile(BenchmarkEvidenceProfile profile)
        => BenchmarkKebabCaseEnumConverter<BenchmarkEvidenceProfile>.Format(profile);

    private static void ApplyDeclarationDocument(BenchmarkEvidenceCatalog catalog, BenchmarkDeclarationDocument document)
    {
        BenchmarkDeclarationService.ValidateOrThrow(document);
        catalog.Libraries = document.Libraries
            .OrderBy(value => value.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        catalog.Comparisons = document.Comparisons.ToArray();
    }

    private static Dictionary<string, string> ResolveLaneParameters(
        BenchmarkRunResult result,
        IDictionary<string, string>? parameters)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> item in parameters ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(item.Key) || string.IsNullOrWhiteSpace(item.Value))
                throw new ArgumentException("Lane parameters need a name and a value.", nameof(parameters));
            if (item.Key.IndexOf('/') >= 0 || item.Value.IndexOf('/') >= 0 || item.Key.IndexOf('=') >= 0)
                throw new ArgumentException($"Lane parameter '{item.Key}' cannot contain '/' or '='.", nameof(parameters));
            resolved[item.Key.Trim()] = item.Value.Trim();
        }

        string affinity = result.Runner?.Affinity?.Trim() ?? string.Empty;
        if (affinity.Length > 0)
        {
            if (resolved.TryGetValue("affinity", out string? declared) &&
                !string.Equals(declared, affinity, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Lane parameter affinity '{declared}' conflicts with the runner's recorded affinity '{affinity}'.");
            }
            resolved["affinity"] = affinity;
        }
        return resolved;
    }

    private static BenchmarkPolicyEvaluation Evaluate(
        BenchmarkEvidenceCatalog catalog,
        BenchmarkRunResult result,
        string comparisonId,
        string framework,
        BenchmarkEvidenceProfile profile,
        bool publish)
        => BenchmarkPublishPolicyEvaluator.Evaluate(new BenchmarkPolicyLane
        {
            Result = result,
            ComparisonId = comparisonId,
            Framework = framework,
            Profile = profile,
            Publish = publish,
            Definition = catalog.Comparisons.FirstOrDefault(value =>
                string.Equals(value.ComparisonId, comparisonId, StringComparison.OrdinalIgnoreCase)),
            Libraries = catalog.Libraries,
            Policy = catalog.Policy ?? BenchmarkPublishPolicy.CreateDefault()
        });

    private static void ApplyEvaluation(BenchmarkEvidenceEntry entry, BenchmarkPolicyEvaluation evaluation)
    {
        entry.Runner = evaluation.Runner;
        entry.Provenance = evaluation.Provenance;
        entry.Cells = evaluation.Cells;
        entry.PolicyIssues = evaluation.LaneIssues;
        entry.Completeness = evaluation.Completeness;
    }

    private static BenchmarkSupersededLane CreateSuperseded(BenchmarkEvidenceEntry entry, DateTimeOffset supersededBy)
        => new()
        {
            LaneKey = entry.LaneKey,
            ComparisonId = entry.ComparisonId,
            Platform = entry.Platform,
            Framework = entry.Framework,
            Profile = entry.Profile,
            GeneratedUtc = entry.GeneratedUtc,
            SupersededByUtc = supersededBy,
            Publish = entry.Publish,
            ResultPath = entry.ResultPath,
            ResultSha256 = entry.ResultSha256
        };

    private static BenchmarkSupersededLane[] NormalizeSuperseded(
        IEnumerable<BenchmarkSupersededLane> superseded,
        IReadOnlyCollection<BenchmarkEvidenceEntry> current)
        => superseded
            .Where(lane => !current.Any(entry =>
                string.Equals(entry.LaneKey, lane.LaneKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.ResultSha256, lane.ResultSha256, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(lane => lane.LaneKey + "\u001f" + lane.ResultSha256.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(lane => lane.SupersededByUtc).First())
            .OrderBy(lane => lane.LaneKey, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(lane => lane.GeneratedUtc)
            .ToArray();
}
