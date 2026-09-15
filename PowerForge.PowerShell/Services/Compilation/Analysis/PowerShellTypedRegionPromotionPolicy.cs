namespace PowerForge;

/// <summary>
/// Fail-closed promotion policy for terminal returns and prefixes transferring supported local values.
/// Both contracts require parameter inputs, local mutations, and no modeled failure route.
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
        // The error host changes CLR returns into stream writes. Report the missing
        // host contract before the resulting void return obscures that dependency.
        if (lowered.RequiresPowerShellStatementErrors)
            return Reject("region.statement-errors", "The candidate requires a PowerShell statement-error host not represented by the helper ABI.");
        if (candidate.ContinuationLocals.Length > 0 && !HasCompleteContinuationResult(candidate))
            return Reject("region.continuation-result", "The helper does not return the exact ordered local transfer contract.");
        if (!HasCompleteInputLocalContract(candidate))
            return Reject("region.input-local-contract", "The helper local inputs are not exact authored or guarded-prefix transfers.");
        if (candidate.ContinuationLocals.Length > 0 && !candidate.RequiresLocalOwnershipGuard &&
            !UpdatesEstablishedInputLocals(candidate))
            return Reject("region.local-ownership", "Local output requires either fresh invocation-local ownership or exact established input-local targets.");
        var transfersMultipleLocals = candidate.ContinuationLocals.Length > 1;
        if (!transfersMultipleLocals && lowered.OutputCardinality != PowerShellOutputCardinality.Scalar)
            return Reject("region.return-cardinality", "The candidate does not return exactly one scalar value on every accepted path.");
        if (transfersMultipleLocals ? lowered.ReturnType != typeof(object[]) : !PowerShellRegionTransferTypePolicy.IsSupported(lowered.ReturnType))
            return Reject("region.return-type", $"The candidate return type '{lowered.ReturnType.FullName ?? lowered.ReturnType.Name}' is not a supported region transfer type.");
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
        if (graph.ScriptBlocks.Count != 0 || graph.Regions.Count != 1)
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
        var expectedStreams = candidate.ContinuationLocals.Length > 0 ? Array.Empty<string>() : new[] { "Success" };
        if (!region.Streams.SequenceEqual(expectedStreams, StringComparer.Ordinal))
            return Reject("region.stream-contract", $"The candidate stream set [{string.Join(", ", region.Streams)}] does not match its terminal-output or local-transfer contract.");
        if (candidate.ContinuationLocals.Length > 0 && !region.Outputs.SequenceEqual(
                candidate.ContinuationLocals.Select(static local => "transfer:Local:" + local.Name).OrderBy(static name => name, StringComparer.Ordinal),
                StringComparer.Ordinal))
            return Reject("region.continuation-outputs", "The canonical region graph does not expose exactly the returned local-storage targets.");
        var allowedInputs = candidate.InputParameters.Select(static parameter => "Parameter:" + parameter.Name)
            .Concat(candidate.InputLocals.Select(static local => "Local:" + local.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!region.Inputs.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(allowedInputs))
            return Reject("region.input-transfer", "The candidate live inputs do not exactly match its function-parameter and local transfer contract.");
        if (!region.Mutations.All(static mutation => mutation.StartsWith("Local:", StringComparison.Ordinal)))
            return Reject("region.mutation", "The candidate mutates state outside its region-local values.");
        if (candidate.ContinuationLocals.Length > 0 &&
            region.Mutations.Any(mutation => !candidate.ContinuationLocals.Any(local =>
                mutation.Equals("Local:" + local.Name, StringComparison.OrdinalIgnoreCase))))
            return Reject("region.continuation-transfer", "The prefix mutates a local outside its continuation transfer.");
        return new PowerShellTypedRegionPromotionDecision(
            isSafe: true,
            "region.promoted",
            candidate.ContinuationLocals.Length == 0
                ? "The candidate satisfies the bounded terminal region-transfer contract."
                : candidate.RequiresLocalOwnershipGuard
                    ? "The candidate satisfies the bounded prefix continuation contract when its invocation-local targets are fresh; otherwise its original statements execute in place."
                    : "The candidate satisfies complete live-in and live-out transfer contracts for established invocation-local targets.");
    }

    private static PowerShellTypedRegionPromotionDecision Reject(string code, string reason)
        => new(isSafe: false, code, reason);

    private static bool HasCompleteContinuationResult(PowerShellBoundRegionCandidate candidate)
    {
        if (candidate.RegionFunction.Body.Statements.LastOrDefault() is not PowerShellBoundRegionTransferStatement tail)
            return false;
        var values = tail.Locals.ToArray();
        return values.Length == candidate.ContinuationLocals.Length && values.Select((value, index) =>
            value is PowerShellBoundVariableExpression variable && variable.Symbol.Kind == PowerShellSymbolKind.Local &&
            variable.Symbol.Name.Equals(candidate.ContinuationLocals[index].Name, StringComparison.OrdinalIgnoreCase) &&
            PowerShellRegionTransferTypePolicy.IsSupported(variable.Type.ClrType) &&
            (variable.Type.ClrType.FullName ?? variable.Type.ClrType.Name) == candidate.ContinuationLocals[index].TypeName).All(static valid => valid);
    }

    private static bool HasCompleteInputLocalContract(PowerShellBoundRegionCandidate candidate)
        => candidate.InputLocals.All(local =>
            !string.IsNullOrWhiteSpace(local.Name) &&
            ((local.HasTypeConstraint && !string.IsNullOrWhiteSpace(local.TypeConstraintSyntax)) ||
             (!local.HasTypeConstraint && string.IsNullOrWhiteSpace(local.TypeConstraintSyntax) &&
              candidate.AllowsPrefixOwnedInputLocals)) &&
            candidate.RegionFunction.Parameters.Any(parameter =>
                parameter.Symbol.Kind == PowerShellSymbolKind.Local &&
                parameter.Symbol.Name.Equals(local.Name, StringComparison.OrdinalIgnoreCase) &&
                (parameter.Type.ClrType.FullName ?? parameter.Type.ClrType.Name).Equals(local.TypeName, StringComparison.Ordinal) &&
                PowerShellRegionTransferTypePolicy.IsSupported(parameter.Type.ClrType))) &&
           candidate.InputLocals.Select(static local => local.Name)
               .Distinct(StringComparer.OrdinalIgnoreCase).Count() == candidate.InputLocals.Length;

    private static bool UpdatesEstablishedInputLocals(PowerShellBoundRegionCandidate candidate)
        => candidate.InputLocals.Length > 0 && candidate.ContinuationLocals.All(output =>
            candidate.InputLocals.Any(input =>
                input.Name.Equals(output.Name, StringComparison.OrdinalIgnoreCase) &&
                input.TypeName.Equals(output.TypeName, StringComparison.Ordinal) &&
                input.HasTypeConstraint && output.HasTypeConstraint &&
                input.TypeConstraintSyntax.Equals(output.TypeConstraintSyntax, StringComparison.Ordinal)));
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
