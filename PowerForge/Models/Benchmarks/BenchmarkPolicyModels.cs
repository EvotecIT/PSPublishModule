using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// How a runner measured a result. Recorded honestly so readers can judge the evidence.
/// </summary>
public sealed class BenchmarkRunnerInfo
{
    /// <summary>Runner kind: benchmarkdotnet, rotated, powershell-suite or process.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Runner version.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Reported statistic: median or mean.</summary>
    public string Statistic { get; set; } = string.Empty;

    /// <summary>Warmup iterations per cell.</summary>
    public int? Warmups { get; set; }

    /// <summary>Measured iterations per cell and launch.</summary>
    public int? Iterations { get; set; }

    /// <summary>Process launches per cell.</summary>
    public int? Launches { get; set; }

    /// <summary>Ordering of measured work: sequential, rotated, randomized or grouped-rotated.</summary>
    public string Ordering { get; set; } = string.Empty;

    /// <summary>True when cells were measured inside the runner process.</summary>
    public bool? InProcess { get; set; }

    /// <summary>Processor affinity mask applied to measured processes, such as 0xffff0000.</summary>
    public string Affinity { get; set; } = string.Empty;

    /// <summary>Process priority applied to measured processes.</summary>
    public string Priority { get; set; } = string.Empty;
}

/// <summary>Validation status of a library's output.</summary>
[JsonConverter(typeof(BenchmarkKebabCaseEnumConverter<BenchmarkValidationStatus>))]
public enum BenchmarkValidationStatus
{
    /// <summary>The output matched the expected result.</summary>
    Passed,

    /// <summary>The output did not match the expected result.</summary>
    Failed
}

/// <summary>Output validation performed before timing a library.</summary>
public sealed class BenchmarkOutputValidation
{
    /// <summary>Library identifier.</summary>
    public string Library { get; set; } = string.Empty;

    /// <summary>Case the validation covers; empty when it covers every case of the library.</summary>
    public string Case { get; set; } = string.Empty;

    /// <summary>Validation status. Required: a record without one is not a validation outcome.</summary>
    public BenchmarkValidationStatus? Status { get; set; }

    /// <summary>What was checked, such as a checksum, or why it failed.</summary>
    public string Detail { get; set; } = string.Empty;
}

/// <summary>Confidence interval of a reported statistic.</summary>
public sealed class BenchmarkConfidenceInterval
{
    /// <summary>Confidence level, such as 0.95.</summary>
    public double Level { get; set; }

    /// <summary>Lower bound in milliseconds.</summary>
    public double LowerMs { get; set; }

    /// <summary>Upper bound in milliseconds.</summary>
    public double UpperMs { get; set; }
}

/// <summary>
/// Thresholds a lane and its cells must meet before publication. Stored in every catalog so
/// readers see the rules the evidence was held to.
/// </summary>
public sealed class BenchmarkPublishPolicy
{
    /// <summary>Minimum warmup iterations recorded by the runner.</summary>
    public int MinimumWarmups { get; set; } = 3;

    /// <summary>Minimum measured samples behind every publishable cell.</summary>
    public int MinimumMeasuredSamples { get; set; } = 10;

    /// <summary>Confidence level of the recorded interval of the median.</summary>
    public double ConfidenceLevel { get; set; } = 0.95;

    /// <summary>Profiles that may be published.</summary>
    public BenchmarkEvidenceProfile[] PublishableProfiles { get; set; } =
        { BenchmarkEvidenceProfile.Standard, BenchmarkEvidenceProfile.Full };

    /// <summary>Runner kinds accepted for the full profile.</summary>
    public string[] FullProfileRunnerKinds { get; set; } = { "benchmarkdotnet" };

    /// <summary>Library restriction classes whose cells may be published.</summary>
    public BenchmarkLibraryRestriction[] PublishableRestrictions { get; set; } = { BenchmarkLibraryRestriction.Open };

    /// <summary>Cells need passed output validation.</summary>
    public bool RequireOutputValidation { get; set; } = true;

    /// <summary>Published lanes need a comparison declaration and declared libraries.</summary>
    public bool RequireDeclaration { get; set; } = true;

    /// <summary>Every non-subject library needs a recorded package version and hash.</summary>
    public bool RequirePackageHashes { get; set; } = true;

    /// <summary>Creates the default policy.</summary>
    public static BenchmarkPublishPolicy CreateDefault() => new();

    /// <summary>
    /// Lists every way this policy is weaker than the standard policy. Projects may tighten the standard for
    /// published evidence but never relax it.
    /// </summary>
    /// <returns>Descriptions of the weaker settings; empty when the policy is at least as strict.</returns>
    public string[] DescribeWeakerThanStandard()
    {
        BenchmarkPublishPolicy standard = CreateDefault();
        var issues = new List<string>();
        if (MinimumWarmups < standard.MinimumWarmups)
            issues.Add($"minimum warmups {MinimumWarmups} is below {standard.MinimumWarmups}.");
        if (MinimumMeasuredSamples < standard.MinimumMeasuredSamples)
            issues.Add($"minimum measured samples {MinimumMeasuredSamples} is below {standard.MinimumMeasuredSamples}.");
        if (ConfidenceLevel < standard.ConfidenceLevel || ConfidenceLevel >= 1)
            issues.Add($"confidence level {ConfidenceLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)} must be at least {standard.ConfidenceLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)} and below 1.");
        if ((PublishableProfiles ?? Array.Empty<BenchmarkEvidenceProfile>()).Except(standard.PublishableProfiles).Any())
            issues.Add("only the standard and full profiles may be published.");
        if ((FullProfileRunnerKinds ?? Array.Empty<string>()).Except(standard.FullProfileRunnerKinds, StringComparer.OrdinalIgnoreCase).Any())
            issues.Add("the full profile accepts only BenchmarkDotNet runners.");
        if ((PublishableRestrictions ?? Array.Empty<BenchmarkLibraryRestriction>()).Except(standard.PublishableRestrictions).Any())
            issues.Add("only open libraries may be published.");
        if (!RequireOutputValidation)
            issues.Add("output validation is required.");
        if (!RequireDeclaration)
            issues.Add("a comparison declaration is required.");
        if (!RequirePackageHashes)
            issues.Add("package versions and hashes are required.");
        return issues.ToArray();
    }
}

/// <summary>Serializes enums as kebab-case strings, such as publish-restricted.</summary>
/// <typeparam name="T">Enum type.</typeparam>
public sealed class BenchmarkKebabCaseEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    /// <inheritdoc />
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected a string value for {typeof(T).Name}.");
        string text = reader.GetString() ?? string.Empty;
        if (TryParse(text, out T value))
            return value;
        throw new JsonException($"'{text}' is not a valid {typeof(T).Name} value. Expected: {string.Join(", ", Enum.GetNames(typeof(T)).Select(ToKebab))}.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(ToKebab(value.ToString()));

    /// <summary>Parses a kebab-case or PascalCase name.</summary>
    /// <param name="text">Text to parse.</param>
    /// <param name="value">Parsed value.</param>
    /// <returns>True when the text names a value.</returns>
    public static bool TryParse(string? text, out T value)
    {
        string compact = (text ?? string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).Trim();
        foreach (string name in Enum.GetNames(typeof(T)))
        {
            if (string.Equals(name, compact, StringComparison.OrdinalIgnoreCase))
            {
                value = (T)Enum.Parse(typeof(T), name);
                return true;
            }
        }
        value = default;
        return false;
    }

    /// <summary>Formats a value as kebab-case.</summary>
    /// <param name="value">Value.</param>
    /// <returns>Kebab-case name.</returns>
    public static string Format(T value) => ToKebab(value.ToString());

    private static string ToKebab(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length + 4);
        for (int index = 0; index < name.Length; index++)
        {
            char character = name[index];
            if (char.IsUpper(character) && index > 0)
                builder.Append('-');
            builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }
}
