using System.Management.Automation.Language;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Builds portable failure maps, audit trails, IR artifacts, and diagnostics policy evidence.</summary>
internal static class PowerShellCompilationDiagnosticsEvidenceBuilder
{
    private static readonly Regex BuildDiagnostic = new(
        @"(?<file>[^\r\n\(]+)\((?<line>\d+),(?<column>\d+)\)\s*:\s*(?:fatal\s+)?(?:error|warning)\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<message>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex RuntimeLocation = new(
        @"\bin\s+(?<file>[^\r\n]+?):line\s+(?<line>\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex SecretAssignment = new(
        "(?<name>password|passwd|pwd|token|api[_-]?key|secret)\\s*[:=]\\s*(?:\"[^\"]*\"|'[^']*'|[^\\s;,]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static PowerShellCompilationFailureMap CreateFailureMap(
        PowerShellCompilationPlan plan,
        IEnumerable<PowerShellCompiledMethod> methods,
        PowerShellCompilationUnitDispositionLedger ledger)
    {
        var entries = new List<PowerShellCompilationFailureMapEntry>();
        foreach (var method in methods.Where(static item => item.Lifecycle is null)
                     .OrderBy(static item => item.DocumentId, StringComparer.Ordinal)
                     .ThenBy(static item => item.SourceLine)
                     .ThenBy(static item => item.SourceName, StringComparer.Ordinal))
        {
            var file = plan.Files.FirstOrDefault(item => PowerShellCompilationPathSafety.PathEquals(item.FullPath, method.SourcePath));
            if (file is null) continue;
            var unit = file.Units.FirstOrDefault(item => item.Kind == PowerShellCompilationUnitKind.Function &&
                item.Name.Equals(method.SourceName, StringComparison.OrdinalIgnoreCase) && item.StartLine == method.SourceLine)
                ?? file.Units.FirstOrDefault(item => item.Name.Equals(method.SourceName, StringComparison.OrdinalIgnoreCase));
            if (unit is null) continue;
            var unitId = PowerShellCompilationExplanationService.ComputeUnitId(file.RelativePath, unit);
            var ledgerEntry = ledger.Entries.FirstOrDefault(item => item.UnitId.Equals(unitId, StringComparison.Ordinal));
            var boundary = DescribeBoundary(ledgerEntry);
            var maps = method.SourceMap.Length == 0
                ? new[] { new PowerShellCompilationSourceMapEntry(method.SourceLine, method.SourceColumn, method.SourceEndLine, method.SourceEndColumn, 1, 1, 1, 1) }
                : method.SourceMap;
            entries.AddRange(maps.Select(map => new PowerShellCompilationFailureMapEntry
            {
                DocumentId = method.DocumentId,
                RelativePath = NormalizeRelative(file.RelativePath, Path.GetFileName(file.FullPath)),
                UnitId = unitId,
                UnitName = unit.Name,
                GeneratedMemberName = method.GeneratedName,
                SourceStartLine = map.SourceStartLine,
                SourceStartColumn = map.SourceStartColumn,
                SourceEndLine = map.SourceEndLine,
                SourceEndColumn = map.SourceEndColumn,
                GeneratedStartLine = map.GeneratedStartLine,
                GeneratedEndLine = map.GeneratedEndLine,
                BoundaryContract = boundary
            }));
        }
        var mappedUnitIds = entries.Select(static item => item.UnitId).ToHashSet(StringComparer.Ordinal);
        var identityRoot = plan.Files.Length == 0
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(Path.GetFullPath(plan.Files[0].FullPath)) ?? Directory.GetCurrentDirectory();
        foreach (var disposition in ledger.Entries
                     .Where(entry => !mappedUnitIds.Contains(entry.UnitId))
                     .OrderBy(static entry => entry.RelativePath, StringComparer.Ordinal)
                     .ThenBy(static entry => entry.StartLine)
                     .ThenBy(static entry => entry.Name, StringComparer.Ordinal))
        {
            var file = plan.Files.FirstOrDefault(item =>
                NormalizeRelative(item.RelativePath, Path.GetFileName(item.FullPath)).Equals(disposition.RelativePath, StringComparison.OrdinalIgnoreCase));
            if (file is null) continue;
            var unit = file.Units.FirstOrDefault(item =>
                PowerShellCompilationExplanationService.ComputeUnitId(file.RelativePath, item).Equals(disposition.UnitId, StringComparison.Ordinal));
            if (unit is null) continue;
            var extent = GetUnitExtent(file.FullPath, unit);
            entries.Add(new PowerShellCompilationFailureMapEntry
            {
                DocumentId = PowerShellSourceParser.CreateDocumentId(file.FullPath, identityRoot),
                RelativePath = NormalizeRelative(file.RelativePath, Path.GetFileName(file.FullPath)),
                UnitId = disposition.UnitId,
                UnitName = disposition.Name,
                GeneratedMemberName = disposition.GeneratedMemberName,
                SourceStartLine = extent.StartLine,
                SourceStartColumn = extent.StartColumn,
                SourceEndLine = extent.EndLine,
                SourceEndColumn = extent.EndColumn,
                GeneratedStartLine = 0,
                GeneratedEndLine = 0,
                BoundaryContract = DescribeBoundary(disposition)
            });
        }
        var map = new PowerShellCompilationFailureMap
        {
            Entries = entries.OrderBy(static item => item.DocumentId, StringComparer.Ordinal)
                .ThenBy(static item => item.SourceStartLine)
                .ThenBy(static item => item.SourceStartColumn)
                .ThenBy(static item => item.GeneratedMemberName, StringComparer.Ordinal)
                .ToArray()
        };
        map.Sha256 = ComputeFailureMapSha256(map);
        return map;
    }

    internal static PowerShellCompilationAuditTrail CreateAuditTrail(
        PowerShellCompilationBuildSpec spec,
        PowerShellCompilationBuildCacheEvidence cache,
        PowerShellCompilationDependencyGraph graph,
        PowerShellCompilationAbiManifest? abi,
        PowerShellCompilationUnitDispositionLedger ledger,
        IEnumerable<PowerShellCompilationCommandProviderContract> providers)
    {
        var events = new List<PowerShellCompilationAuditEvent>
        {
            Event("Cache", cache.Reason, cache.Hit ? "Hit" : spec.UseBuildCache ? "MissOrStore" : "Bypassed", cache.Key),
            Event("DependencyGraph", spec.ExpectedDependencyLock is null ? "NoReviewedBaseline" : "ReviewedLockMatched", spec.ExpectedDependencyLock is null ? "RecordedUnreviewed" : "Matched", graph.LockSha256),
            Event("Abi", string.IsNullOrWhiteSpace(spec.ExpectedPublicAbiSha256) ? "NoExpectedAbi" : "ExpectedAbiMatched", abi is null ? "NotApplicable" : string.IsNullOrWhiteSpace(spec.ExpectedPublicAbiSha256) ? "Recorded" : "Matched", abi?.Sha256 ?? string.Empty),
            Event("FallbackCrossings", ledger.BoundaryCrossings == 0 ? "NoBoundaryCrossings" : "BoundedCrossingsRecorded", ledger.BoundaryCrossings.ToString(System.Globalization.CultureInfo.InvariantCulture), string.Empty)
        };
        events.AddRange(providers
            .OrderBy(static provider => provider.ProviderId, StringComparer.Ordinal)
            .ThenBy(static provider => provider.ProviderVersion, StringComparer.Ordinal)
            .ThenBy(static provider => provider.CommandName, StringComparer.Ordinal)
            .Select(static provider => Event("ProviderSelection", "LockedCompileTimeContract", "Selected", provider.ProviderId + "@" + provider.ProviderVersion + ":" + provider.CommandName)));
        var trail = new PowerShellCompilationAuditTrail
        {
            Events = events.OrderBy(static item => item.Category, StringComparer.Ordinal)
                .ThenBy(static item => item.Subject, StringComparer.Ordinal)
                .ThenBy(static item => item.Reason, StringComparer.Ordinal)
                .ToArray()
        };
        trail.Sha256 = ComputeAuditTrailSha256(trail);
        return trail;
    }

    internal static PowerShellCompilationDiagnosticsPolicy CreatePolicy()
        => new()
        {
            RedactedData = new[]
            {
                "AbsolutePaths",
                "AuthoredSourceText",
                "CredentialsAndSecrets",
                "EnvironmentVariables",
                "HostedExecutableSource",
                "MachineOwnedState",
                "ParserAstObjects"
            }
        };

    internal static PowerShellCompilationIrSnapshotEvidence PublishIrSnapshots(
        string stagingDirectory,
        string artifactName,
        PowerShellCompilationIrSnapshotBundle? snapshots,
        out PowerShellCompilationArtifactFile? artifactFile)
    {
        artifactFile = null;
        if (snapshots is null)
            return new PowerShellCompilationIrSnapshotEvidence { Emitted = false };
        var fileName = artifactName + ".powerforge-ir.json";
        var path = Path.Combine(stagingDirectory, fileName);
        File.WriteAllText(path, PowerShellCompilationIrSnapshotBuilder.Serialize(snapshots, indented: true), new UTF8Encoding(false));
        artifactFile = new PowerShellCompilationArtifactFile
        {
            Path = path,
            RelativePath = fileName,
            Role = "CompilerIrSnapshot",
            Sha256 = ComputeFileSha256(path),
            SizeBytes = new FileInfo(path).Length
        };
        return new PowerShellCompilationIrSnapshotEvidence
        {
            Emitted = true,
            RelativePath = fileName,
            Sha256 = artifactFile.Sha256
        };
    }

    internal static PowerShellCompilationFailure MapFailure(
        PowerShellCompilationFailureStage stage,
        string reason,
        string summary,
        string output,
        int? exitCode,
        PowerShellCompilationPlan? plan,
        PowerShellCompilationFailureMap? failureMap)
    {
        var locations = new List<PowerShellCompilationFailureLocation>();
        if (failureMap is not null)
        {
            foreach (Match match in BuildDiagnostic.Matches(output ?? string.Empty))
            {
                var entry = MatchEntry(failureMap, match.Groups["file"].Value.Trim(), Parse(match.Groups["line"].Value));
                if (entry is null) continue;
                locations.Add(Location(entry, match.Groups["code"].Value, match.Groups["message"].Value));
            }
            foreach (Match match in RuntimeLocation.Matches(output ?? string.Empty))
            {
                var entry = MatchEntry(failureMap, match.Groups["file"].Value.Trim(), Parse(match.Groups["line"].Value));
                if (entry is null) continue;
                locations.Add(Location(entry, "RuntimeFailure", summary));
            }
        }
        var redactedSummary = Redact(plan, summary);
        return new PowerShellCompilationFailure
        {
            Stage = stage,
            Reason = reason,
            Summary = redactedSummary,
            ExitCode = exitCode,
            Locations = locations.GroupBy(static item => item.RelativePath + "\0" + item.Line + "\0" + item.Column + "\0" + item.Code, StringComparer.Ordinal)
                .Select(static group => group.First())
                .OrderBy(static item => item.RelativePath, StringComparer.Ordinal)
                .ThenBy(static item => item.Line)
                .ThenBy(static item => item.Column)
                .ToArray()
        };
    }

    internal static string Redact(PowerShellCompilationPlan? plan, string value)
    {
        var redacted = value ?? string.Empty;
        if (plan is not null)
        {
            foreach (var file in plan.Files.OrderByDescending(static item => item.FullPath.Length))
            {
                redacted = Replace(redacted, file.FullPath, NormalizeRelative(file.RelativePath, Path.GetFileName(file.FullPath)));
                redacted = Replace(redacted, file.FullPath.Replace('\\', '/'), NormalizeRelative(file.RelativePath, Path.GetFileName(file.FullPath)));
            }
        }
        redacted = SecretAssignment.Replace(redacted, static match => match.Groups["name"].Value + "=<redacted-secret>");
        return Regex.Replace(redacted, "(?<![A-Za-z0-9_])(?:[A-Za-z]:[\\\\/]|/|\\\\\\\\)[^\\s\\)\\]\\}\\\"']+", "<redacted-path>", RegexOptions.CultureInvariant);
    }

    internal static string Hash<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(json))
            .Select(static item => item.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    internal static string ComputeFailureMapSha256(PowerShellCompilationFailureMap map)
        => Hash(new { map.SchemaVersion, map.Entries });

    internal static string ComputeAuditTrailSha256(PowerShellCompilationAuditTrail trail)
        => Hash(new { trail.SchemaVersion, trail.Events });

    private static PowerShellCompilationFailureMapEntry? MatchEntry(PowerShellCompilationFailureMap map, string file, int line)
    {
        var normalized = file.Replace('\\', '/');
        return map.Entries.FirstOrDefault(item =>
                   item.DocumentId.Equals(normalized, StringComparison.Ordinal) &&
                   line >= item.SourceStartLine && line <= Math.Max(item.SourceStartLine, item.SourceEndLine))
               ?? map.Entries.FirstOrDefault(item =>
                   item.RelativePath.Equals(normalized, StringComparison.OrdinalIgnoreCase) &&
                   line >= item.SourceStartLine && line <= Math.Max(item.SourceStartLine, item.SourceEndLine))
               ?? map.Entries.FirstOrDefault(item =>
                   normalized.EndsWith("/" + item.RelativePath, StringComparison.OrdinalIgnoreCase) &&
                   line >= item.SourceStartLine && line <= Math.Max(item.SourceStartLine, item.SourceEndLine))
               ?? map.Entries.FirstOrDefault(item => item.GeneratedMemberName.Equals(normalized, StringComparison.Ordinal));
    }

    private static PowerShellCompilationFailureLocation Location(PowerShellCompilationFailureMapEntry entry, string code, string message)
        => new()
        {
            RelativePath = entry.RelativePath,
            UnitId = entry.UnitId,
            UnitName = entry.UnitName,
            Line = entry.SourceStartLine,
            Column = entry.SourceStartColumn,
            Code = code,
            Message = Redact(null, message),
            BoundaryContract = entry.BoundaryContract
        };

    private static string DescribeBoundary(PowerShellCompilationUnitDisposition? entry)
        => entry is null ? string.Empty
            : entry.Emitted && entry.RuntimeRouted ? "TypedClr+PowerShellRuntime"
            : entry.Emitted ? "TypedClr"
            : entry.RuntimeRouted ? "PowerShellRuntime"
            : entry.Rejected ? "Rejected"
            : "Omitted";

    private static PowerShellCompilationAuditEvent Event(string category, string reason, string outcome, string subject)
        => new() { Category = category, Reason = reason, Outcome = outcome, Subject = subject };

    private static int Parse(string value) => int.TryParse(value, out var parsed) ? parsed : 0;

    private static (int StartLine, int StartColumn, int EndLine, int EndColumn) GetUnitExtent(
        string path,
        PowerShellCompilationUnitPlan unit)
    {
        try
        {
            var ast = Parser.ParseFile(path, out _, out _);
            IScriptExtent extent = ast.Extent;
            if (unit.Kind == PowerShellCompilationUnitKind.Function)
            {
                var function = ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
                    .OfType<FunctionDefinitionAst>()
                    .FirstOrDefault(candidate =>
                        candidate.Name.Equals(unit.Name, StringComparison.OrdinalIgnoreCase) &&
                        candidate.Body.Extent.StartLineNumber == unit.StartLine);
                if (function is not null) extent = function.Body.Extent;
            }
            return (extent.StartLineNumber, extent.StartColumnNumber, extent.EndLineNumber, extent.EndColumnNumber);
        }
        catch (IOException)
        {
            return (unit.StartLine, 1, unit.StartLine, 1);
        }
        catch (UnauthorizedAccessException)
        {
            return (unit.StartLine, 1, unit.StartLine, 1);
        }
    }
    private static string NormalizeRelative(string path, string fallback)
        => string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) ? fallback.Replace('\\', '/') : path.Replace('\\', '/');

    private static string Replace(string value, string oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(oldValue)) return value;
        var index = value.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            value = value.Substring(0, index) + newValue + value.Substring(index + oldValue.Length);
            index = value.IndexOf(oldValue, index + newValue.Length, StringComparison.OrdinalIgnoreCase);
        }
        return value;
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(stream).Select(static item => item.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }
}

/// <summary>Maps a recorded artifact runtime failure back to authored source locations.</summary>
public static class PowerShellCompilationFailureMapper
{
    /// <summary>Maps a redacted runtime log through the manifest's statement-level source and boundary contracts.</summary>
    public static PowerShellCompilationFailure MapRuntimeFailure(PowerShellCompilationArtifactManifest manifest, string failureText)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        if (failureText is null) throw new ArgumentNullException(nameof(failureText));
        return PowerShellCompilationDiagnosticsEvidenceBuilder.MapFailure(
            PowerShellCompilationFailureStage.Runtime,
            "RuntimeFailure",
            "The generated artifact reported a runtime failure.",
            failureText,
            exitCode: null,
            plan: null,
            manifest.FailureMap);
    }
}
