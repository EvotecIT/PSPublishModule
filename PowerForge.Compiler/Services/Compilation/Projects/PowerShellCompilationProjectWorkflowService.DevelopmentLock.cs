using System.Text.Json;

namespace PowerForge;

public sealed partial class PowerShellCompilationProjectWorkflowService
{
    // A development build may edit already-declared local scripts and ordinary resource bytes.
    // It may not change graph topology, module metadata, binary identities, policy or external closure.
    // The reviewed lock stays unchanged; the artifact records the exact effective source graph.
    internal static void EnsureDevelopmentLockMatches(
        PowerShellCompilationDependencyGraph reviewed,
        PowerShellCompilationDependencyGraph current,
        string sourceRoot,
        string projectRoot)
    {
        PowerShellCompilationDependencyLockHasher.EnsureValid(reviewed, nameof(reviewed));
        PowerShellCompilationDependencyLockHasher.EnsureValid(current, nameof(current));
        if (reviewed.LockSha256.Equals(current.LockSha256, StringComparison.OrdinalIgnoreCase)) return;
        var normalized = JsonSerializer.Deserialize<PowerShellCompilationDependencyGraph>(
            JsonSerializer.Serialize(current))!;
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in normalized.Nodes)
        {
            if (!CanEdit(node)) continue;
            var candidates = reviewed.Nodes.Where(old => old.Kind == node.Kind &&
                old.Identity.Source.Equals(node.Identity.Source, StringComparison.Ordinal)).ToArray();
            if (candidates.Length != 1 || !CanEdit(candidates[0])) continue;
            var baseline = candidates[0];
            replacements.Add(node.Id, baseline.Id);
            node.Id = baseline.Id;
            node.Identity.Sha256 = baseline.Identity.Sha256;
        }
        normalized.RootNodeId = Map(normalized.RootNodeId);
        foreach (var edge in normalized.Edges)
        {
            edge.FromId = Map(edge.FromId);
            edge.ToId = Map(edge.ToId);
        }
        if (!PowerShellCompilationDependencyLockHasher.ComputeSha256(normalized)
                .Equals(reviewed.LockSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Project dependencies or declarations changed. Review project lock and run project restore before run/watch; " +
                "only content edits to already-locked local scripts and resources are accepted automatically.");

        string Map(string id) => replacements.TryGetValue(id, out var replacement) ? replacement : id;

        bool CanEdit(PowerShellCompilationDependencyNode node)
        {
            if (!node.Exists || node.Identity.Provenance != "LocalReadOnlyResolution" ||
                node.Kind is not (PowerShellCompilationDependencyNodeKind.Script or
                    PowerShellCompilationDependencyNodeKind.ScriptModule or
                    PowerShellCompilationDependencyNodeKind.Content or
                    PowerShellCompilationDependencyNodeKind.TypeData or
                    PowerShellCompilationDependencyNodeKind.FormatData)) return false;
            var path = Path.GetFullPath(Path.Combine(sourceRoot, node.Identity.Source.Replace('/', Path.DirectorySeparatorChar)));
            PowerShellCompilationPathSafety.EnsureContained(projectRoot, path, "Development input escapes the project root.");
            PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(path, "Development input traverses a link.");
            return true;
        }
    }
}
