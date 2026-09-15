using System.Management.Automation.Language;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>
/// Applies only immutable region decisions produced by the canonical bound pipeline. This artifact
/// shaper validates source identity and containment but never re-decides semantic eligibility.
/// </summary>
internal static class PowerShellHybridRegionRewriter
{
    internal static PowerShellHybridSourceEdit[] CreateEdits(
        string sourcePath,
        ScriptBlockAst ast,
        PowerShellTypedCompilationResult typed,
        ISet<string> removedFunctionKeys)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        var regions = typed.PromotedRegions
            .Where(region => PowerShellCompilationPathSafety.PathEquals(region.SourcePath, fullPath))
            .OrderBy(static region => region.StartOffset)
            .ToArray();
        if (regions.Length == 0) return Array.Empty<PowerShellHybridSourceEdit>();
        var source = ast.Extent.Text;
        var sourceDocumentSha256 = ComputeSha256(source);
        var functions = ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
            .Cast<FunctionDefinitionAst>()
            .ToArray();
        var expectedDocumentId = PowerShellSourceParser.CreateDocumentId(
            fullPath,
            Path.GetDirectoryName(Path.GetFullPath(typed.SourcePaths.FirstOrDefault() ?? typed.SourcePath)));
        var edits = new List<PowerShellHybridSourceEdit>();
        var hosted = new Dictionary<FunctionDefinitionAst, List<(PowerShellCompiledRegion Region, string Replacement)>>();
        foreach (var region in regions)
        {
            var owner = functions.SingleOrDefault(function =>
                function.Name.Equals(region.SourceName, StringComparison.OrdinalIgnoreCase) &&
                function.Body.Extent.StartLineNumber == region.SourceLine);
            if (owner is null)
                throw new InvalidOperationException($"Promoted region '{region.RegionId}' no longer has its retained function owner.");
            var ownerKey = PowerShellHybridModuleComposer.GetCompiledMethodKey(fullPath, owner.Name, owner.Body.Extent.StartLineNumber);
            if (removedFunctionKeys.Contains(ownerKey))
                throw new InvalidOperationException($"Promoted region '{region.RegionId}' overlaps a function selected for whole-method emission.");
            if (!region.DocumentId.Equals(expectedDocumentId, StringComparison.Ordinal) ||
                !region.SourceDocumentSha256.Equals(sourceDocumentSha256, StringComparison.Ordinal) ||
                region.StartOffset < owner.Body.Extent.StartOffset ||
                region.EndOffset > owner.Body.Extent.EndOffset ||
                region.EndOffset <= region.StartOffset ||
                region.EndOffset > source.Length)
                throw new InvalidOperationException($"Promoted region '{region.RegionId}' does not match its immutable authored source boundary.");
            var regionText = source.Substring(region.StartOffset, region.EndOffset - region.StartOffset);
            if (!ComputeSha256(regionText).Equals(region.SourceSha256, StringComparison.Ordinal))
                throw new InvalidOperationException($"Promoted region '{region.RegionId}' source changed after semantic selection.");
            if (!HasSafeGraph(region.RegionGraph))
                throw new InvalidOperationException($"Promoted region '{region.RegionId}' does not carry a fail-closed typed boundary graph.");
            if (region.InputLocals.Any(static local =>
                    !local.HasTypeConstraint || string.IsNullOrWhiteSpace(local.TypeConstraintSyntax)))
                throw new InvalidOperationException($"Promoted region '{region.RegionId}' is missing its authored input-local type constraint.");
            var inputs = region.InputParameters.Select(static parameter => "${" + parameter.Name + "}")
                .Concat(region.InputLocals.Select(static local => "${" + local.Name + "}"));
            if (region.RequiresPowerShellStopping)
                inputs = inputs.Append("[PowerForge.Generated.Runtime.PowerShellStatementErrorContext]::CreateLoopInterrupt($ExecutionContext)");
            var arguments = string.Join(", ", inputs);
            if (region.ContinuationLocals.Any(static local =>
                    local.HasTypeConstraint && string.IsNullOrWhiteSpace(local.TypeConstraintSyntax)))
                throw new InvalidOperationException($"Promoted region '{region.RegionId}' is missing its authored continuation type constraint.");
            var receiver = region.ContinuationLocals.Count == 0
                ? "return "
                : string.Join(", ", region.ContinuationLocals.Select(static local =>
                    (local.HasTypeConstraint ? local.TypeConstraintSyntax : string.Empty) + "${" + local.Name + "}")) + " = ";
            var invocation = receiver + "[" + typed.NamespaceName + "." + typed.TypeName + "]::" +
                             region.GeneratedName + "(" + arguments + ")";
            if (region.ContinuationLocals.Count > 0 && !region.RequiresLocalOwnershipGuard &&
                region.ContinuationLocals.Any(output => !region.InputLocals.Any(input =>
                    input.Name.Equals(output.Name, StringComparison.OrdinalIgnoreCase) &&
                    input.TypeName.Equals(output.TypeName, StringComparison.Ordinal) &&
                    input.HasTypeConstraint && output.HasTypeConstraint &&
                    input.TypeConstraintSyntax.Equals(output.TypeConstraintSyntax, StringComparison.Ordinal))))
                throw new InvalidOperationException($"Promoted region '{region.RegionId}' writes a local absent from its established input-local contract.");
            if (region.RequiresLocalOwnershipGuard)
            {
                if (region.ContinuationLocals.Count == 0)
                    throw new InvalidOperationException($"Promoted region '{region.RegionId}' has no local targets for its ownership condition.");
            }
            if (regions.Any(candidate => candidate.RequiresLocalOwnershipGuard &&
                    candidate.SourceName.Equals(region.SourceName, StringComparison.OrdinalIgnoreCase) && candidate.SourceLine == region.SourceLine))
            {
                if (!hosted.TryGetValue(owner, out var replacements))
                    hosted.Add(owner, replacements = new List<(PowerShellCompiledRegion Region, string Replacement)>());
                replacements.Add((region, invocation));
                continue;
            }
            edits.Add(new PowerShellHybridSourceEdit(
                region.StartOffset,
                region.EndOffset - region.StartOffset,
                invocation,
                region.RegionId));
        }
        foreach (var pair in hosted)
        {
            var owner = pair.Key;
            var replacements = pair.Value;
            // A condition does not reset $? after a successful void expression. Keep the
            // authored declaration's status while native definition owns scope and aliases.
            var registration = owner.Extent.Text + "\nif ([PowerForge.Generated.Runtime.PowerShellNativeFunctionHost]::InstallDeclaredFunction($ExecutionContext.SessionState.Module, " +
                Quote(owner.Name) + ", [PowerForge.Generated.Runtime.PowerShellHybridRegionHost]::Create($ExecutionContext.SessionState.Module, " +
                Quote(source) + ", $PSCommandPath, " + owner.Extent.StartOffset + ", " + owner.Extent.EndOffset +
                ", [int[]]@(" + string.Join(", ", replacements.Select(static item => item.Region.StartOffset)) +
                "), [int[]]@(" + string.Join(", ", replacements.Select(static item => item.Region.EndOffset)) +
                "), [string[]]@(" + string.Join(", ", replacements.Select(item => Quote(item.Replacement))) +
                "), [bool[]]@(" + string.Join(", ", replacements.Select(static item => item.Region.RequiresLocalOwnershipGuard ? "$true" : "$false")) +
                "), [string[]]@(" + string.Join(", ", replacements.SelectMany(static item => item.Region.InputLocals).Select(local => Quote(local.Name))) +
                "), [string[]]@(" + string.Join(", ", replacements.SelectMany(static item => item.Region.InputLocals).Select(local => Quote(local.TypeName))) +
                "), [int[]]@(" + string.Join(", ", replacements.Select(static item => item.Region.InputLocals.Count)) +
                "), [string[]]@(" + string.Join(", ", replacements.Where(static item => item.Region.RequiresLocalOwnershipGuard)
                    .SelectMany(static item => item.Region.ContinuationLocals).Select(local => Quote(local.Name))) + ")))) { }\n";
            edits.Add(new PowerShellHybridSourceEdit(owner.Extent.StartOffset,
                owner.Extent.EndOffset - owner.Extent.StartOffset, registration, replacements[0].Region.RegionId));
        }
        edits.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        EnsureNonOverlapping(edits);
        return edits.ToArray();
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    private static bool HasSafeGraph(PowerShellCompilationRegionGraph graph)
        => graph.ScriptBlocks.Count == 0 && graph.Regions.Count == 1 &&
           graph.Regions[0].Execution == PowerShellCompilationRegionExecution.Typed &&
           graph.Regions[0].Errors.Count == 0 &&
           graph.Regions[0].HostedCommandBoundarySites == 0 &&
           graph.Regions[0].ModuleStateReadBoundarySites == 0 &&
           graph.Regions[0].ModuleStateWriteBoundarySites == 0;

    private static void EnsureNonOverlapping(IReadOnlyList<PowerShellHybridSourceEdit> edits)
    {
        for (var index = 1; index < edits.Count; index++)
        {
            var previous = edits[index - 1];
            if (edits[index].Start < previous.Start + previous.Length)
                throw new InvalidOperationException(
                    $"Promoted regions '{previous.Identity}' and '{edits[index].Identity}' overlap.");
        }
    }

    private static string ComputeSha256(string value)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value))
            .Select(static item => item.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }
}

internal readonly struct PowerShellHybridSourceEdit
{
    internal PowerShellHybridSourceEdit(int start, int length, string replacement, string identity)
    {
        Start = start;
        Length = length;
        Replacement = replacement ?? string.Empty;
        Identity = identity ?? string.Empty;
    }

    internal int Start { get; }
    internal int Length { get; }
    internal string Replacement { get; }
    internal string Identity { get; }
}
