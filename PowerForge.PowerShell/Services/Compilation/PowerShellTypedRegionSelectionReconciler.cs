namespace PowerForge;

/// <summary>
/// Reconciles policy-approved regions with final whole-method selection so public evidence never
/// claims that a helper dropped by CLR member shaping was promoted.
/// </summary>
internal static class PowerShellTypedRegionSelectionReconciler
{
    internal static PowerShellTypedRegionSelection Resolve(
        IReadOnlyList<PowerShellCompiledRegion> approved,
        IReadOnlyList<PowerShellCompilationRegionCandidate> candidates,
        IReadOnlyList<PowerShellCompiledMethod> methods,
        ICollection<PowerShellCompilationDiagnostic> diagnostics)
    {
        var covered = approved.Where(region => methods.Any(method =>
                method.SourceName.Equals(region.SourceName, StringComparison.OrdinalIgnoreCase) &&
                method.SourceLine == region.SourceLine &&
                PowerShellCompilationPathSafety.PathEquals(method.SourcePath, region.SourcePath)))
            .Select(static region => region.RegionId).ToHashSet(StringComparer.Ordinal);
        var colliding = approved
            .Where(region => !covered.Contains(region.RegionId))
            .Where(region => methods.Any(method => method.GeneratedName.Equals(region.GeneratedName, StringComparison.Ordinal)))
            .ToArray();
        foreach (var region in colliding)
        {
            diagnostics.Add(new PowerShellCompilationDiagnostic(
                PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                $"Typed region in function '{region.SourceName}' collides with generated CLR method name '{region.GeneratedName}'.",
                region.SourcePath,
                region.SourceLine,
                1));
        }

        var collidingIds = colliding.Select(static region => region.RegionId).ToHashSet(StringComparer.Ordinal);
        var enclosed = approved.Where(region => !covered.Contains(region.RegionId) && !collidingIds.Contains(region.RegionId) &&
                approved.Any(outer => outer.RegionId != region.RegionId &&
                    !covered.Contains(outer.RegionId) && !collidingIds.Contains(outer.RegionId) &&
                    outer.SourceName.Equals(region.SourceName, StringComparison.OrdinalIgnoreCase) &&
                    outer.SourceLine == region.SourceLine &&
                    PowerShellCompilationPathSafety.PathEquals(outer.SourcePath, region.SourcePath) &&
                    outer.StartOffset <= region.StartOffset && outer.EndOffset >= region.EndOffset &&
                    outer.EndOffset - outer.StartOffset > region.EndOffset - region.StartOffset))
            .Select(static region => region.RegionId).ToHashSet(StringComparer.Ordinal);
        return new PowerShellTypedRegionSelection(
            approved.Where(region => !covered.Contains(region.RegionId) && !collidingIds.Contains(region.RegionId) && !enclosed.Contains(region.RegionId)).ToArray(),
            candidates.Select(candidate => covered.Contains(candidate.RegionId)
                ? RejectWholeFunctionCoverage(candidate)
                : collidingIds.Contains(candidate.RegionId)
                ? RejectGeneratedNameCollision(candidate)
                : enclosed.Contains(candidate.RegionId)
                ? RejectEnclosedRegion(candidate)
                : candidate).ToArray());
    }

    internal static PowerShellCompilationRegionCandidate CreateEvidence(PowerShellRegionCandidateDecision decision)
    {
        var candidate = decision.Candidate;
        var span = decision.Emission?.SourceSpan ?? candidate.RegionFunction.Body.Span;
        return new PowerShellCompilationRegionCandidate(
            candidate.RegionId,
            candidate.SourceSha256,
            candidate.SourceDocumentSha256,
            candidate.SourceName,
            candidate.SourceLine,
            candidate.SourcePath,
            span.StartOffset,
            span.EndOffset,
            span.StartLine,
            span.StartColumn,
            span.EndLine,
            span.EndColumn,
            decision.Policy.IsSafe,
            decision.Policy.Code,
            decision.Policy.Reason,
            decision.Policy.IsSafe ? decision.Emission?.GeneratedName ?? string.Empty : string.Empty,
            decision.Emission?.RegionGraph,
            candidate.ContinuationLocals.ToArray());
    }

    private static PowerShellCompilationRegionCandidate RejectGeneratedNameCollision(PowerShellCompilationRegionCandidate candidate)
        => new(
            candidate.RegionId,
            candidate.SourceSha256,
            candidate.SourceDocumentSha256,
            candidate.SourceName,
            candidate.SourceLine,
            candidate.SourcePath,
            candidate.StartOffset,
            candidate.EndOffset,
            candidate.StartLine,
            candidate.StartColumn,
            candidate.EndLine,
            candidate.EndColumn,
            promoted: false,
            "region.generated-name-collision",
            $"The generated helper name '{candidate.GeneratedName}' collides with a selected whole-function method.",
            generatedName: string.Empty,
            candidate.RegionGraph,
            candidate.ContinuationLocals);

    private static PowerShellCompilationRegionCandidate RejectWholeFunctionCoverage(PowerShellCompilationRegionCandidate candidate)
        => new(
            candidate.RegionId, candidate.SourceSha256, candidate.SourceDocumentSha256,
            candidate.SourceName, candidate.SourceLine, candidate.SourcePath,
            candidate.StartOffset, candidate.EndOffset, candidate.StartLine, candidate.StartColumn,
            candidate.EndLine, candidate.EndColumn,
            promoted: false,
            "region.whole-function-selected",
            "The containing function is already selected for whole-function emission.",
            generatedName: string.Empty,
            candidate.RegionGraph, candidate.ContinuationLocals);

    private static PowerShellCompilationRegionCandidate RejectEnclosedRegion(PowerShellCompilationRegionCandidate candidate)
        => new(
            candidate.RegionId, candidate.SourceSha256, candidate.SourceDocumentSha256,
            candidate.SourceName, candidate.SourceLine, candidate.SourcePath,
            candidate.StartOffset, candidate.EndOffset, candidate.StartLine, candidate.StartColumn,
            candidate.EndLine, candidate.EndColumn,
            promoted: false,
            "region.enclosed-by-approved-region",
            "A larger approved region covers this candidate's complete authored span.",
            generatedName: string.Empty,
            candidate.RegionGraph, candidate.ContinuationLocals);
}

internal sealed class PowerShellTypedRegionSelection
{
    internal PowerShellTypedRegionSelection(
        PowerShellCompiledRegion[] promoted,
        PowerShellCompilationRegionCandidate[] candidates)
    {
        Promoted = promoted ?? Array.Empty<PowerShellCompiledRegion>();
        Candidates = candidates ?? Array.Empty<PowerShellCompilationRegionCandidate>();
    }

    internal PowerShellCompiledRegion[] Promoted { get; }
    internal PowerShellCompilationRegionCandidate[] Candidates { get; }
}
