using System.Globalization;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>How a line of generated .NET restore/build output is reported.</summary>
public enum PowerShellCompilationBuildLineKind
{
    /// <summary>Ordinary progress output, kept for verbose output and failure context.</summary>
    Other,
    /// <summary>An MSBuild, NuGet or compiler warning.</summary>
    Warning,
    /// <summary>An MSBuild, NuGet or compiler error.</summary>
    Error
}

/// <summary>
/// Turns a <see cref="PowerShellCompilationBuildResult"/> into short human-readable summaries for the CLI and cmdlet:
/// the error lines of a failed build instead of the full MSBuild transcript, and on success the artifact, target,
/// unit counts and the units that still run as PowerShell script.
/// </summary>
public static class PowerShellCompilationBuildReport
{
    /// <summary>Default number of error lines or fallback units listed before the rest are counted.</summary>
    public const int DefaultMaximumItems = 20;

    private const int MaximumContextLines = 10;

    private static readonly Regex Diagnostic = new(
        // "file(1,2): error CS0103: ...", "x.csproj : error NU1101: ...", "error MSB1009: ...", "error: ..."
        @"(?:(?:^|[\s:(\]])(?:fatal\s+)?(?<kind>error|warning)\s+[A-Za-z]+\d+\s*:)|(?:(?:^\s*|:\s*)(?<kind>error|warning)\s*:)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex ProjectSuffix = new(
        @"\s+\[[^\[\]]+\.(?:csproj|proj|sln|slnx|props|targets)(?:::[^\[\]]*)?\]\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Classifies one line of generated <c>dotnet restore</c>/<c>build</c>/<c>publish</c> output.</summary>
    /// <param name="line">The output line.</param>
    public static PowerShellCompilationBuildLineKind ClassifyBuildLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return PowerShellCompilationBuildLineKind.Other;
        var match = Diagnostic.Match(line);
        if (!match.Success) return PowerShellCompilationBuildLineKind.Other;
        return match.Groups["kind"].Value.Equals("error", StringComparison.OrdinalIgnoreCase)
            ? PowerShellCompilationBuildLineKind.Error
            : PowerShellCompilationBuildLineKind.Warning;
    }

    /// <summary>Trims a diagnostic line and drops MSBuild's trailing <c>[project.csproj]</c> context.</summary>
    /// <param name="line">The output line.</param>
    public static string NormalizeBuildLine(string line)
        => ProjectSuffix.Replace((line ?? string.Empty).Trim(), string.Empty);

    /// <summary>Returns the distinct error lines found in build output, in the order they first appear.</summary>
    /// <param name="output">Combined restore/build output or an error message that embeds it.</param>
    public static IReadOnlyList<string> GetErrorLines(string? output)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lines = new List<string>();
        foreach (var line in SplitLines(output))
        {
            if (ClassifyBuildLine(line) != PowerShellCompilationBuildLineKind.Error) continue;
            var normalized = NormalizeBuildLine(line);
            if (seen.Add(normalized)) lines.Add(normalized);
        }
        return lines;
    }

    /// <summary>
    /// Describes every authored unit that is not compiled into typed code, as <c>file:line [feature] name</c>,
    /// with <c>(omitted)</c> for units left out of a Strict artifact.
    /// </summary>
    /// <param name="manifest">Artifact manifest of a successful build.</param>
    public static IReadOnlyList<string> GetFallbackUnits(PowerShellCompilationArtifactManifest? manifest)
    {
        var entries = manifest?.UnitDispositionLedger?.Entries;
        if (entries is null) return Array.Empty<string>();
        return entries
            .Where(static entry => !entry.Emitted)
            .Select(static entry =>
            {
                var cause = entry.DiagnosticChain.FirstOrDefault();
                var line = cause is { Line: > 0 } ? cause.Line : entry.StartLine;
                var reason = cause is not null && !string.IsNullOrWhiteSpace(cause.FeatureId)
                    ? $" [{cause.FeatureId}]"
                    : FirstNonEmpty(entry.DependencyCauses, entry.BoundaryCauses) is { } other ? $" [{other}]" : string.Empty;
                var omitted = entry.Omitted ? " (omitted)" : string.Empty;
                return $"{entry.RelativePath}:{line.ToString(CultureInfo.InvariantCulture)}{reason} {entry.Name}{omitted}";
            })
            .ToArray();
    }

    /// <summary>
    /// Builds the summary of a failed build: what failed, authored source locations mapped from the generated
    /// build, and the distinct error lines (bounded), followed by <paramref name="fullOutputHint"/>.
    /// The full output stays on <see cref="PowerShellCompilationBuildResult.BuildOutput"/>.
    /// </summary>
    /// <param name="result">Failed build result.</param>
    /// <param name="fullOutputHint">Optional last line telling the user where the full output is.</param>
    /// <param name="maximumItems">Maximum error lines listed before the rest are counted.</param>
    public static string FormatFailure(PowerShellCompilationBuildResult result, string? fullOutputHint = null, int maximumItems = DefaultMaximumItems)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        var errorText = result.Error ?? string.Empty;
        var headline = SplitLines(errorText).Select(static line => line.Trim()).FirstOrDefault(static line => line.Length > 0)
            ?? "PowerShell artifact build failed.";
        var lines = new List<string>
        {
            result.Failure is { } failure
                ? $"PowerShell artifact build failed during {failure.Stage}: {headline}"
                : $"PowerShell artifact build failed: {headline}"
        };

        var locations = result.Failure?.Locations ?? Array.Empty<PowerShellCompilationFailureLocation>();
        if (locations.Length > 0)
        {
            lines.Add("Source locations:");
            AddBounded(lines, locations.Select(static location =>
                $"{location.RelativePath}:{location.Line}:{location.Column} [{location.Code}] {location.Message}" +
                (string.IsNullOrWhiteSpace(location.UnitName) ? string.Empty : $" (in {location.UnitName})")), maximumItems, "location(s)");
        }

        // Prefer the build output: the error message is redacted for evidence and may embed the same lines.
        var errors = GetErrorLines(result.BuildOutput).ToArray();
        if (errors.Length == 0)
            errors = GetErrorLines(errorText).Where(error => !error.Equals(headline, StringComparison.Ordinal)).ToArray();
        if (errors.Length > 0)
        {
            lines.Add($"Build errors ({errors.Length}):");
            AddBounded(lines, errors, maximumItems, "error line(s)");
        }
        else if (!string.IsNullOrWhiteSpace(result.BuildOutput))
        {
            // No recognizable error line (a crashed tool, a timeout): show the end of the output for context.
            var tail = SplitLines(result.BuildOutput).Where(static line => !string.IsNullOrWhiteSpace(line)).ToArray();
            lines.Add("Last build output:");
            lines.AddRange(tail.Skip(Math.Max(0, tail.Length - MaximumContextLines)).Select(static line => "  " + line.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(result.BuildWorkspace))
            lines.Add($"Build workspace kept at: {result.BuildWorkspace}");
        if (!string.IsNullOrWhiteSpace(fullOutputHint))
            lines.Add(fullOutputHint!);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Writes the summary of a successful build: artifact path, mode, target, size, typed vs. script unit counts and
    /// elapsed time, then the units that still run as script (bounded at Info, the full list at Verbose).
    /// </summary>
    /// <param name="logger">Destination logger.</param>
    /// <param name="result">Successful build result.</param>
    /// <param name="elapsed">Total build time.</param>
    /// <param name="maximumFallbackUnits">Maximum fallback units listed at Info.</param>
    public static void WriteSuccess(ILogger logger, PowerShellCompilationBuildResult result, TimeSpan elapsed, int maximumFallbackUnits = DefaultMaximumItems)
    {
        if (logger is null) throw new ArgumentNullException(nameof(logger));
        if (result is null) throw new ArgumentNullException(nameof(result));
        var manifest = result.Manifest;
        if (manifest is null)
        {
            logger.Success($"Built {result.ArtifactPath} in {PowerShellCompilationBuildProgress.Format(elapsed)}");
            return;
        }

        logger.Success($"Built {manifest.Kind} ({manifest.Mode}) in {PowerShellCompilationBuildProgress.Format(elapsed)}: {result.ArtifactPath}");
        logger.Info($"Target: {DescribeTarget(manifest)}; size {FormatSize(manifest.ArtifactSizeBytes)}");
        logger.Info(DescribeUnits(manifest));
        logger.Info($"Manifest: {result.ManifestPath}");
        if (!string.IsNullOrWhiteSpace(result.GeneratedSourcePath))
            logger.Info($"Generated source: {result.GeneratedSourcePath}");

        var fallbacks = GetFallbackUnits(manifest);
        if (fallbacks.Count == 0) return;
        logger.Info(manifest.Mode == PowerShellCompilationMode.Package
            ? $"Units packaged as PowerShell script ({fallbacks.Count}):"
            : $"Units not compiled to typed code ({fallbacks.Count}):");
        var shown = Math.Max(0, maximumFallbackUnits);
        foreach (var fallback in fallbacks.Take(shown)) logger.Info("  " + fallback);
        if (fallbacks.Count <= shown) return;
        logger.Info($"  ... and {fallbacks.Count - shown} more{(logger.IsVerbose ? string.Empty : " (verbose output lists all)")}");
        foreach (var fallback in fallbacks.Skip(shown)) logger.Verbose("  " + fallback);
    }

    private static string DescribeTarget(PowerShellCompilationArtifactManifest manifest)
    {
        var parts = new List<string> { manifest.TargetFramework };
        if (!string.IsNullOrWhiteSpace(manifest.RuntimeIdentifier)) parts.Add(manifest.RuntimeIdentifier!);
        if (manifest.Kind == PowerShellCompilationArtifactKind.Executable)
        {
            parts.Add(manifest.SelfContained ? "self-contained" : "framework-dependent");
            if (manifest.SingleFile) parts.Add("single file");
            if (manifest.Optimization != PowerShellCompilationExecutableOptimization.None) parts.Add(manifest.Optimization.ToString());
        }
        return string.Join(", ", parts.Where(static part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string DescribeUnits(PowerShellCompilationArtifactManifest manifest)
    {
        var script = manifest.AnalyzedUnits - manifest.EmittedUnits - manifest.OmittedUnits;
        var text = $"Units: {manifest.EmittedUnits} of {manifest.AnalyzedUnits} compiled to typed code";
        if (script > 0) text += $", {script} run as PowerShell script";
        if (manifest.OmittedUnits > 0) text += $", {manifest.OmittedUnits} omitted";
        if (manifest.PromotedTypedRegions > 0) text += $", {manifest.PromotedTypedRegions} typed region(s) inside script units";
        text += manifest.RequiresPowerShellRuntime ? "; needs the PowerShell runtime" : "; no PowerShell runtime dependency";
        return text;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024) return (bytes / (1024d * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        if (bytes >= 1024) return (bytes / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        return bytes.ToString(CultureInfo.InvariantCulture) + " B";
    }

    private static void AddBounded(List<string> lines, IEnumerable<string> items, int maximum, string noun)
    {
        var all = items.ToArray();
        var shown = Math.Max(0, maximum);
        lines.AddRange(all.Take(shown).Select(static item => "  " + item));
        if (all.Length > shown) lines.Add($"  ... and {all.Length - shown} more {noun}");
    }

    private static string? FirstNonEmpty(params IReadOnlyList<string>[] lists)
        => lists.SelectMany(static list => list).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    private static IEnumerable<string> SplitLines(string? text)
        => (text ?? string.Empty).Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
}
