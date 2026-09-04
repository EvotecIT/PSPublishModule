namespace PowerForge;

/// <summary>
/// Fail-closed promotion policy for a bound terminal suffix. The first contract permits only a
/// scalar success value, parameter inputs, region-local mutations, and no modeled failure route.
/// </summary>
internal static class PowerShellTypedRegionPromotionPolicy
{
    internal static PowerShellTypedRegionPromotionDecision Evaluate(
        PowerShellBoundRegionCandidate candidate,
        PowerShellLoweredFunction lowered,
        PowerShellCSharpMethodEmission emitted)
    {
        if (candidate is null) throw new ArgumentNullException(nameof(candidate));
        if (lowered is null) throw new ArgumentNullException(nameof(lowered));
        if (emitted is null) throw new ArgumentNullException(nameof(emitted));
        if (lowered.OutputCardinality != PowerShellOutputCardinality.Scalar)
            return Reject("region.return-cardinality", "The candidate does not return exactly one scalar value on every accepted path.");
        if (!PowerShellStableScalarTypePolicy.IsSupported(lowered.ReturnType))
            return Reject("region.return-type", $"The candidate return type '{lowered.ReturnType.FullName ?? lowered.ReturnType.Name}' is not a stable scalar transfer type.");
        if (lowered.RequiresPowerShellStreams)
            return Reject("region.stream-contract", "The candidate requires PowerShell stream semantics beyond the single scalar Success result contract.");
        if (lowered.RequiresProviderCancellation)
            return Reject("region.provider-cancellation", "The candidate requires provider cancellation and cannot execute as an isolated helper.");
        if (lowered.RequiresPowerShellCommandRegions)
            return Reject("region.command-boundary", "The candidate contains a PowerShell-hosted command boundary.");
        if (lowered.RequiresPowerShellRuntimeState)
            return Reject("region.runtime-state", "The candidate depends on PowerShell runtime state not represented by the helper ABI.");
        if (lowered.RequiresPowerShellModuleState)
            return Reject("region.module-state", "The candidate depends on retained module state not represented by the helper ABI.");
        if (emitted.SourceSpan.StartOffset != candidate.RegionFunction.Body.Span.StartOffset ||
            emitted.SourceSpan.EndOffset != candidate.RegionFunction.Body.Span.EndOffset)
            return Reject("region.source-span", "The emitted method span does not match the exact authored candidate span.");

        var graph = emitted.RegionGraph;
        if (graph.Regions.Count != 1)
            return Reject("region.graph-shape", "The candidate did not lower to exactly one canonical region.");
        var region = graph.Regions[0];
        if (region.Execution != PowerShellCompilationRegionExecution.Typed)
            return Reject("region.execution-route", "The canonical region route is not fully typed.");
        if (region.HostedCommandBoundarySites != 0 ||
            region.ModuleStateReadBoundarySites != 0 ||
            region.ModuleStateWriteBoundarySites != 0)
            return Reject("region.static-boundary", "The candidate contains a hosted command or module-state boundary.");
        if (region.Errors.Count != 0)
            return Reject("region.error-route", $"The candidate has modeled error route(s): {string.Join(", ", region.Errors)}.");
        if (!region.Streams.SequenceEqual(new[] { "Success" }, StringComparer.Ordinal))
            return Reject("region.stream-contract", $"The candidate stream set [{string.Join(", ", region.Streams)}] is outside the single Success result contract.");
        if (!region.Inputs.All(static input => input.StartsWith("Parameter:", StringComparison.Ordinal)))
            return Reject("region.input-transfer", "The candidate reads a live value that is not a retained-function parameter.");
        if (!region.Mutations.All(static mutation => mutation.StartsWith("Local:", StringComparison.Ordinal)))
            return Reject("region.mutation", "The candidate mutates state outside its region-local values.");
        return new PowerShellTypedRegionPromotionDecision(
            isSafe: true,
            "region.promoted",
            "The candidate satisfies the bounded terminal scalar promotion contract.");
    }

    private static PowerShellTypedRegionPromotionDecision Reject(string code, string reason)
        => new(isSafe: false, code, reason);
}

internal sealed class PowerShellTypedRegionPromotionDecision
{
    internal PowerShellTypedRegionPromotionDecision(bool isSafe, string code, string reason)
    {
        IsSafe = isSafe;
        Code = code ?? string.Empty;
        Reason = reason ?? string.Empty;
    }

    internal bool IsSafe { get; }
    internal string Code { get; }
    internal string Reason { get; }
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
