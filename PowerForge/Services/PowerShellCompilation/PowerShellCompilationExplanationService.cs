using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>Creates deterministic, relocation-safe decision traces from the canonical compilation plan.</summary>
public static class PowerShellCompilationExplanationService
{
    /// <summary>Explains every unit without re-running or duplicating compiler eligibility logic.</summary>
    public static PowerShellCompilationExplanation Create(PowerShellCompilationPlan plan)
        => CreateCore(plan, artifactKind: null, shapedCompilation: null, finalShape: false);

    /// <summary>
    /// Creates the pre-ledger compatibility approximation of artifact shaping.
    /// Use the ledger overload for final artifact evidence.
    /// </summary>
    [Obsolete("This compatibility overload cannot represent immutable post-shaping evidence. Use CreateFinal(PowerShellCompilationPlan, PowerShellCompilationUnitDispositionLedger).")]
    public static PowerShellCompilationExplanation CreateFinal(
        PowerShellCompilationPlan plan,
        PowerShellCompilationArtifactKind artifactKind,
        PowerShellTypedCompilationResult? shapedCompilation)
    {
        if (shapedCompilation?.Methods.Any(static method => method.RequiresPowerShellModuleState) == true)
            throw new InvalidOperationException(
                "The pre-ledger explanation overload cannot represent directional parent-module state evidence. " +
                "Create the immutable final unit-disposition ledger and use the ledger overload.");
        var explanation = CreateCore(plan, artifactKind, shapedCompilation, finalShape: true);
        explanation.SchemaVersion = 2;
        explanation.SemanticCompatibilityVersion = 1;
        explanation.SemanticFingerprintSha256 = ComputeSemanticFingerprint(explanation);
        return explanation;
    }

    /// <summary>Explains final artifact disposition exclusively from the immutable post-shaping ledger.</summary>
    public static PowerShellCompilationExplanation CreateFinal(
        PowerShellCompilationPlan plan,
        PowerShellCompilationUnitDispositionLedger ledger)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (ledger is null) throw new ArgumentNullException(nameof(ledger));
        var explanation = CreateCore(plan, artifactKind: null, shapedCompilation: null, finalShape: false);
        var entries = ledger.Entries.ToDictionary(static entry => entry.UnitId, StringComparer.Ordinal);
        foreach (var unit in explanation.Files.SelectMany(static file => file.Units))
        {
            if (!entries.TryGetValue(unit.UnitId, out var entry))
                throw new InvalidOperationException($"Final unit disposition ledger is missing authored unit '{unit.UnitId}'.");
            unit.SemanticEligible = entry.SemanticEligible;
            unit.Emitted = entry.Emitted;
            unit.RetainedHostedSource = entry.RetainedHostedSource;
            unit.RuntimeCommandRegions = entry.RuntimeCommandRegions;
            unit.ModuleStateReadBoundaryCrossings = entry.ModuleStateReadBoundaryCrossings;
            unit.ModuleStateWriteBoundaryCrossings = entry.ModuleStateWriteBoundaryCrossings;
            unit.BoundaryCrossings = entry.BoundaryCrossings;
            unit.ShapingFallback = entry.ShapingFallback;
            unit.Omitted = entry.Omitted;
            unit.Rejected = entry.Rejected;
            unit.Decision = entry.Rejected
                ? PowerShellCompilationDecisionKind.Rejected
                : entry.RuntimeRouted
                    ? PowerShellCompilationDecisionKind.RuntimeFallback
                    : PowerShellCompilationDecisionKind.Typed;
            unit.LoweringRoute = entry.Emitted && entry.RuntimeRouted
                ? "BoundClr+PowerShellRuntime"
                : entry.Emitted
                    ? "BoundClr"
                    : entry.RuntimeRouted
                        ? "PowerShellRuntime"
                        : "Rejected";
            unit.ArtifactDisposition = entry.ArtifactDisposition;
            unit.Causes = entry.DiagnosticChain.Select(static cause => new PowerShellCompilationExplanationDiagnostic
            {
                Code = cause.Code,
                FeatureId = cause.FeatureId,
                Message = cause.Message,
                Line = cause.Line,
                Column = cause.Column
            }).ToArray();
        }
        explanation.CanProceed = plan.CanProceed && ledger.RejectedUnits == 0;
        explanation.TypedUnits = ledger.EmittedUnits;
        explanation.RuntimeFallbackUnits = ledger.RuntimeRoutedUnits;
        explanation.RejectedUnits = ledger.RejectedUnits;
        explanation.SemanticFingerprintSha256 = ComputeSemanticFingerprint(explanation);
        return explanation;
    }

    private static PowerShellCompilationExplanation CreateCore(
        PowerShellCompilationPlan plan,
        PowerShellCompilationArtifactKind? artifactKind,
        PowerShellTypedCompilationResult? shapedCompilation,
        bool finalShape)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (artifactKind.HasValue && !Enum.IsDefined(typeof(PowerShellCompilationArtifactKind), artifactKind.Value))
            throw new ArgumentOutOfRangeException(nameof(artifactKind));
        var redact = CreateRedactor(plan);
        var files = plan.Files
            .OrderBy(static file => NormalizePath(file.RelativePath), StringComparer.Ordinal)
            .Select(file => CreateFile(plan.Mode, artifactKind, file, shapedCompilation, finalShape, redact))
            .ToArray();
        var units = files.SelectMany(static file => file.Units).ToArray();
        var rejectedUnits = units.Count(static unit => unit.Decision == PowerShellCompilationDecisionKind.Rejected);
        var explanation = new PowerShellCompilationExplanation
        {
            Mode = plan.Mode,
            TargetFramework = plan.TargetFramework ?? string.Empty,
            CanProceed = plan.CanProceed && rejectedUnits == 0,
            TypedUnits = units.Count(static unit => unit.Decision == PowerShellCompilationDecisionKind.Typed),
            RuntimeFallbackUnits = units.Count(static unit => unit.Decision == PowerShellCompilationDecisionKind.RuntimeFallback),
            RejectedUnits = rejectedUnits,
            DependencyCauses = plan.Dependencies
                .Where(static dependency => dependency.Disposition == PowerShellCompilationDependencyDisposition.Missing)
                .OrderBy(static dependency => NormalizePath(dependency.RelativePath), StringComparer.Ordinal)
                .ThenBy(static dependency => dependency.Name, StringComparer.Ordinal)
                .Select(dependency => CreateDependencyExplanation(dependency, redact))
                .ToArray(),
            Dependencies = plan.Dependencies
                .OrderBy(static dependency => NormalizePath(dependency.RelativePath), StringComparer.Ordinal)
                .ThenBy(static dependency => dependency.Name, StringComparer.Ordinal)
                .Select(dependency => CreateDependencyTrace(dependency, redact))
                .ToArray(),
            Files = files
        };
        explanation.SemanticFingerprintSha256 = ComputeSemanticFingerprint(explanation);
        return explanation;
    }

    private static PowerShellCompilationDependencyTrace CreateDependencyTrace(
        PowerShellCompilationDependency dependency,
        Func<string, string> redact)
    {
        var name = redact(dependency.Name);
        return new PowerShellCompilationDependencyTrace
        {
            Name = name,
            RelativePath = NormalizeRelativePath(redact(dependency.RelativePath), name),
            Kind = dependency.Kind,
            Discovery = dependency.Discovery,
            Disposition = dependency.Disposition
        };
    }

    private static PowerShellCompilationDependencyExplanation CreateDependencyExplanation(
        PowerShellCompilationDependency dependency,
        Func<string, string> redact)
    {
        var name = redact(dependency.Name);
        var relativePath = redact(dependency.RelativePath);
        return new PowerShellCompilationDependencyExplanation
        {
            Name = name,
            RelativePath = NormalizeRelativePath(relativePath, name),
            Kind = dependency.Kind,
            Discovery = dependency.Discovery,
            Message = redact(dependency.Note)
        };
    }

    private static PowerShellCompilationFileExplanation CreateFile(
        PowerShellCompilationMode mode,
        PowerShellCompilationArtifactKind? artifactKind,
        PowerShellCompilationFilePlan file,
        PowerShellTypedCompilationResult? shapedCompilation,
        bool finalShape,
        Func<string, string> redact)
    {
        var relativePath = NormalizeRelativePath(file.RelativePath, Path.GetFileName(file.FullPath));
        return new PowerShellCompilationFileExplanation
        {
            RelativePath = relativePath,
            Causes = file.Diagnostics
                .OrderBy(static diagnostic => diagnostic.Line)
                .ThenBy(static diagnostic => diagnostic.Column)
                .ThenBy(static diagnostic => diagnostic.Code)
                .Select(diagnostic => CreateDiagnostic(diagnostic, redact))
                .ToArray(),
            Units = file.Units
                .OrderBy(static unit => unit.StartLine)
                .ThenBy(static unit => unit.Kind)
                .ThenBy(static unit => unit.Name, StringComparer.Ordinal)
                .Select(unit => CreateUnit(mode, artifactKind, file.FullPath, relativePath, unit, shapedCompilation, finalShape, redact))
                .ToArray()
        };
    }

    private static PowerShellCompilationUnitExplanation CreateUnit(
        PowerShellCompilationMode mode,
        PowerShellCompilationArtifactKind? artifactKind,
        string fullPath,
        string relativePath,
        PowerShellCompilationUnitPlan unit,
        PowerShellTypedCompilationResult? shapedCompilation,
        bool finalShape,
        Func<string, string> redact)
    {
        var decision = GetDecision(mode, artifactKind, fullPath, unit, shapedCompilation, finalShape);
        var shapedDiagnostics = finalShape && shapedCompilation is not null
            ? shapedCompilation.Diagnostics.Where(diagnostic =>
                PathEquals(diagnostic.FilePath, fullPath) &&
                diagnostic.Line == unit.StartLine)
            : Enumerable.Empty<PowerShellCompilationDiagnostic>();
        return new PowerShellCompilationUnitExplanation
        {
            UnitId = ComputeUnitId(relativePath, unit),
            Name = unit.Name,
            Kind = unit.Kind,
            StartLine = unit.StartLine,
            ReturnType = unit.ReturnType,
            Parameters = unit.Parameters.Select(static parameter => new PowerShellCompilationExplanationParameter
            {
                Name = parameter.Name,
                TypeName = parameter.TypeName,
                AllowNull = parameter.AllowNull,
                HasDefaultValue = parameter.HasDefaultValue
            }).ToArray(),
            Decision = decision,
            LoweringRoute = decision switch
            {
                PowerShellCompilationDecisionKind.Typed => "BoundClr",
                PowerShellCompilationDecisionKind.RuntimeFallback => "PowerShellRuntime",
                _ => "Rejected"
            },
            ArtifactDisposition = decision switch
            {
                PowerShellCompilationDecisionKind.Typed => "TypedArtifact",
                PowerShellCompilationDecisionKind.RuntimeFallback => "HostedSource",
                _ => "Absent"
            },
            Causes = unit.Diagnostics.Concat(shapedDiagnostics)
                .GroupBy(static diagnostic => diagnostic.Code + "\0" + diagnostic.FeatureId + "\0" + diagnostic.Line + "\0" + diagnostic.Column + "\0" + diagnostic.Message, StringComparer.Ordinal)
                .Select(static group => group.First())
                .OrderBy(static diagnostic => diagnostic.Line)
                .ThenBy(static diagnostic => diagnostic.Column)
                .ThenBy(static diagnostic => diagnostic.Code)
                .ThenBy(static diagnostic => diagnostic.FeatureId, StringComparer.Ordinal)
                .Select(diagnostic => CreateDiagnostic(diagnostic, redact))
                .ToArray()
        };
    }

    private static PowerShellCompilationDecisionKind GetDecision(
        PowerShellCompilationMode mode,
        PowerShellCompilationArtifactKind? artifactKind,
        string fullPath,
        PowerShellCompilationUnitPlan unit,
        PowerShellTypedCompilationResult? shapedCompilation,
        bool finalShape)
    {
        if (!finalShape)
        {
            return unit.IsCompilable
                ? PowerShellCompilationDecisionKind.Typed
                : mode is PowerShellCompilationMode.Package or PowerShellCompilationMode.Hybrid or PowerShellCompilationMode.Analyze
                    ? PowerShellCompilationDecisionKind.RuntimeFallback
                    : PowerShellCompilationDecisionKind.Rejected;
        }
        if (!unit.IsCompilable)
            return mode is PowerShellCompilationMode.Package or PowerShellCompilationMode.Hybrid or PowerShellCompilationMode.Analyze
                ? artifactKind == PowerShellCompilationArtifactKind.Library && mode == PowerShellCompilationMode.Hybrid
                    ? PowerShellCompilationDecisionKind.Rejected
                    : PowerShellCompilationDecisionKind.RuntimeFallback
                : PowerShellCompilationDecisionKind.Rejected;
        if (mode == PowerShellCompilationMode.Package)
            return PowerShellCompilationDecisionKind.RuntimeFallback;
        if (mode == PowerShellCompilationMode.Analyze)
            return PowerShellCompilationDecisionKind.Typed;
        if (mode == PowerShellCompilationMode.Strict && shapedCompilation is null)
            return PowerShellCompilationDecisionKind.Typed;

        var emitted = shapedCompilation?.Methods.Any(method =>
            method.Lifecycle is null &&
            PathEquals(
                string.IsNullOrWhiteSpace(method.SourcePath) ? shapedCompilation.SourcePath : method.SourcePath,
                fullPath) &&
            method.SourceName.Equals(unit.Name, StringComparison.OrdinalIgnoreCase) &&
            method.SourceLine == unit.StartLine) == true;
        if (emitted)
            return PowerShellCompilationDecisionKind.Typed;
        return mode == PowerShellCompilationMode.Hybrid && artifactKind != PowerShellCompilationArtifactKind.Library
            ? PowerShellCompilationDecisionKind.RuntimeFallback
            : PowerShellCompilationDecisionKind.Rejected;
    }

    private static PowerShellCompilationExplanationDiagnostic CreateDiagnostic(
        PowerShellCompilationDiagnostic diagnostic,
        Func<string, string> redact)
        => new()
        {
            Code = diagnostic.Code,
            FeatureId = diagnostic.FeatureId,
            Message = redact(diagnostic.Message),
            Line = diagnostic.Line,
            Column = diagnostic.Column
        };

    internal static string ComputeUnitId(string relativePath, PowerShellCompilationUnitPlan unit)
    {
        var identity = string.Join("\0", relativePath, unit.Kind, unit.Name, unit.StartLine);
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))
            .Take(12)
            .Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static string ComputeSemanticFingerprint(PowerShellCompilationExplanation explanation)
    {
        var builder = new StringBuilder();
        Append(explanation.SemanticCompatibilityVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(explanation.Mode.ToString());
        Append(explanation.TargetFramework);
        Append(explanation.CanProceed ? "true" : "false");
        foreach (var file in explanation.Files.OrderBy(static item => NormalizePath(item.RelativePath), StringComparer.Ordinal))
        {
            Append(NormalizePath(file.RelativePath));
            foreach (var cause in file.Causes
                         .OrderBy(static item => item.Code)
                         .ThenBy(static item => item.FeatureId, StringComparer.Ordinal)
                         .ThenBy(static item => item.Message, StringComparer.Ordinal))
            {
                Append(cause.Code.ToString());
                Append(cause.FeatureId);
                Append(cause.Message);
            }
            foreach (var unit in file.Units
                         .OrderBy(static item => item.Kind)
                         .ThenBy(static item => item.Name, StringComparer.Ordinal)
                         .ThenBy(static item => item.ReturnType, StringComparer.Ordinal))
            {
                Append(unit.Kind.ToString());
                Append(unit.Name);
                Append(unit.ReturnType);
                Append(unit.Decision.ToString());
                Append(unit.LoweringRoute);
                Append(unit.ArtifactDisposition);
                Append(unit.SemanticEligible ? "true" : "false");
                Append(unit.Emitted ? "true" : "false");
                Append(unit.RetainedHostedSource ? "true" : "false");
                Append(unit.RuntimeCommandRegions.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (explanation.SemanticCompatibilityVersion >= 2)
                {
                    Append(unit.ModuleStateReadBoundaryCrossings.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Append(unit.ModuleStateWriteBoundaryCrossings.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                Append(unit.BoundaryCrossings.ToString(System.Globalization.CultureInfo.InvariantCulture));
                foreach (var parameter in unit.Parameters)
                {
                    Append(parameter.Name);
                    Append(parameter.TypeName);
                    Append(parameter.AllowNull ? "true" : "false");
                    Append(parameter.HasDefaultValue ? "true" : "false");
                }
                foreach (var cause in unit.Causes
                             .OrderBy(static item => item.Code)
                             .ThenBy(static item => item.FeatureId, StringComparer.Ordinal)
                             .ThenBy(static item => item.Message, StringComparer.Ordinal))
                {
                    Append(cause.Code.ToString());
                    Append(cause.FeatureId);
                    Append(cause.Message);
                }
            }
        }
        foreach (var dependency in explanation.Dependencies
                     .OrderBy(static item => NormalizePath(item.RelativePath), StringComparer.Ordinal)
                     .ThenBy(static item => item.Name, StringComparer.Ordinal))
        {
            Append(dependency.Name);
            Append(NormalizePath(dependency.RelativePath));
            Append(dependency.Kind.ToString());
            Append(dependency.Discovery.ToString());
            Append(dependency.Disposition.ToString());
        }
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()))
            .Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));

        void Append(string value) => builder.Append(value.Length).Append(':').Append(value).Append('\n');
    }

    private static string NormalizePath(string path)
        => (path ?? string.Empty).Replace('\\', '/');

    private static bool PathEquals(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        return Path.GetFullPath(left).Equals(Path.GetFullPath(right), FrameworkCompatibility.PathStringComparison());
    }

    private static string NormalizeRelativePath(string path, string fallback)
    {
        var normalized = NormalizePath(path);
        return Path.IsPathRooted(normalized) || string.IsNullOrWhiteSpace(normalized)
            ? NormalizePath(fallback)
            : normalized;
    }

    private static Func<string, string> CreateRedactor(PowerShellCompilationPlan plan)
    {
        var replacements = plan.Files.Select(static file => (file.FullPath, NormalizeRelativePath(file.RelativePath, Path.GetFileName(file.FullPath))))
            .Concat(plan.Dependencies.Where(static dependency => !string.IsNullOrWhiteSpace(dependency.SourcePath))
                .Select(static dependency => (dependency.SourcePath!, NormalizeRelativePath(dependency.RelativePath, dependency.Name))))
            .OrderByDescending(static item => item.Item1.Length)
            .ToArray();
        return message =>
        {
            var redacted = message ?? string.Empty;
            foreach (var (fullPath, relativePath) in replacements)
            {
                redacted = Replace(redacted, fullPath, relativePath);
                redacted = Replace(redacted, NormalizePath(fullPath), relativePath);
            }
            return RedactRemainingAbsolutePaths(redacted);
        };
    }

    private static string RedactRemainingAbsolutePaths(string value)
    {
        var builder = new StringBuilder(value);
        for (var index = 0; index + 2 < builder.Length; index++)
        {
            var windows = char.IsLetter(builder[index]) && builder[index + 1] == ':' && builder[index + 2] is '\\' or '/';
            var unix = builder[index] == '/' && !char.IsWhiteSpace(builder[index + 1]) && IsPathBoundary(builder, index);
            var unc = builder[index] == '\\' && builder[index + 1] == '\\' &&
                      !char.IsWhiteSpace(builder[index + 2]) && IsPathBoundary(builder, index);
            if (!windows && !unix && !unc) continue;
            var quote = index > 0 && builder[index - 1] is ('\'' or '"') ? builder[index - 1] : '\0';
            var end = index + (windows ? 3 : unc ? 2 : 1);
            while (end < builder.Length && (quote == '\0'
                       ? !char.IsWhiteSpace(builder[end]) && builder[end] is not ',' and not ';' and not ')'
                       : builder[end] != quote)) end++;
            builder.Remove(index, end - index).Insert(index, "<redacted-path>");
            index += "<redacted-path>".Length - 1;
        }
        return builder.ToString();
    }

    private static bool IsPathBoundary(StringBuilder builder, int index)
        => index == 0 || char.IsWhiteSpace(builder[index - 1]) || builder[index - 1] is '\'' or '"' or '(' or '[' or '=';

    private static string Replace(string value, string oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(oldValue)) return value;
        var start = 0;
        var builder = new StringBuilder();
        while (true)
        {
            var index = value.IndexOf(oldValue, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0) break;
            builder.Append(value, start, index - start).Append(newValue);
            start = index + oldValue.Length;
        }
        return start == 0 ? value : builder.Append(value, start, value.Length - start).ToString();
    }
}
