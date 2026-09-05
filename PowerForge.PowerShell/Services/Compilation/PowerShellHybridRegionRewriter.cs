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
            var arguments = string.Join(", ", region.InputParameters.Select(static parameter => "${" + parameter.Name + "}"));
            var invocation = "return [" + typed.NamespaceName + "." + typed.TypeName + "]::" +
                             region.GeneratedName + "(" + arguments + ")";
            edits.Add(new PowerShellHybridSourceEdit(
                region.StartOffset,
                region.EndOffset - region.StartOffset,
                invocation,
                region.RegionId));
        }
        EnsureNonOverlapping(edits);
        return edits.ToArray();
    }

    private static bool HasSafeGraph(PowerShellCompilationRegionGraph graph)
        => graph.Regions.Count == 1 &&
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
