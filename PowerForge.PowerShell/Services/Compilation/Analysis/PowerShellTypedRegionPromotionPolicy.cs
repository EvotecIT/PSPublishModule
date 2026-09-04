namespace PowerForge;

/// <summary>
/// Fail-closed promotion policy for a bound terminal suffix. The first contract permits only a
/// scalar success value, parameter inputs, region-local mutations, and no modeled failure route.
/// </summary>
internal static class PowerShellTypedRegionPromotionPolicy
{
    internal static bool IsSafe(
        PowerShellBoundRegionCandidate candidate,
        PowerShellLoweredFunction lowered,
        PowerShellCSharpMethodEmission emitted)
    {
        if (candidate is null || lowered is null || emitted is null) return false;
        if (lowered.OutputCardinality != PowerShellOutputCardinality.Scalar ||
            !PowerShellStableScalarTypePolicy.IsSupported(lowered.ReturnType) ||
            lowered.RequiresPowerShellStreams ||
            lowered.RequiresProviderCancellation ||
            lowered.RequiresPowerShellCommandRegions ||
            lowered.RequiresPowerShellRuntimeState ||
            lowered.RequiresPowerShellModuleState)
            return false;
        if (emitted.SourceSpan.StartOffset != candidate.RegionFunction.Body.Span.StartOffset ||
            emitted.SourceSpan.EndOffset != candidate.RegionFunction.Body.Span.EndOffset)
            return false;

        var graph = emitted.RegionGraph;
        if (graph.Regions.Count != 1) return false;
        var region = graph.Regions[0];
        return region.Execution == PowerShellCompilationRegionExecution.Typed &&
               region.HostedCommandBoundarySites == 0 &&
               region.ModuleStateReadBoundarySites == 0 &&
               region.ModuleStateWriteBoundarySites == 0 &&
               region.Errors.Count == 0 &&
               region.Streams.SequenceEqual(new[] { "Success" }, StringComparer.Ordinal) &&
               region.Inputs.All(static input => input.StartsWith("Parameter:", StringComparison.Ordinal)) &&
               region.Mutations.All(static mutation => mutation.StartsWith("Local:", StringComparison.Ordinal));
    }
}

internal sealed class PowerShellPromotedRegionEmission
{
    internal PowerShellPromotedRegionEmission(
        PowerShellBoundRegionCandidate candidate,
        PowerShellBoundFunction analyzed,
        PowerShellLoweredFunction lowered,
        PowerShellCSharpMethodEmission emission)
    {
        Candidate = candidate;
        Analyzed = analyzed;
        Lowered = lowered;
        Emission = emission;
    }

    internal PowerShellBoundRegionCandidate Candidate { get; }
    internal PowerShellBoundFunction Analyzed { get; }
    internal PowerShellLoweredFunction Lowered { get; }
    internal PowerShellCSharpMethodEmission Emission { get; }
}
