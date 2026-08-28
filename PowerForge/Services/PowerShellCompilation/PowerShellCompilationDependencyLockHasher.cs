using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>Creates and verifies the canonical hash for a PowerShell compilation dependency lock.</summary>
public static class PowerShellCompilationDependencyLockHasher
{
    /// <summary>Computes the canonical lock hash from graph identities and edges.</summary>
    public static string ComputeSha256(PowerShellCompilationDependencyGraph graph)
    {
        if (graph is null) throw new ArgumentNullException(nameof(graph));
        var builder = new StringBuilder();
        Append("graph", graph.SchemaVersion, graph.RootNodeId);
        foreach (var node in graph.Nodes.OrderBy(static item => item.Id, StringComparer.Ordinal))
        {
            Append("node", node.Id, node.Kind, node.Roles, node.Disposition, node.Exists,
                node.Identity.Name, node.Identity.Version, node.Identity.MinimumVersion,
                node.Identity.RequiredVersion, node.Identity.MaximumVersion, node.Identity.Guid,
                node.Identity.Sha256, node.Identity.Source,
                node.Identity.Edition, node.Identity.TargetFramework, node.Identity.RuntimeIdentifier,
                node.Identity.Architecture, node.Identity.PublicKeyToken, node.Identity.Culture, node.Identity.Provenance, node.Identity.InteropAdapter,
                node.Identity.ApartmentState, node.Policy.Redistribution,
                node.Policy.Publisher, node.Policy.Signature, node.Policy.Servicing, node.Policy.License,
                node.Interop.Owner, node.Interop.Platform, node.Interop.Errors, node.Interop.Cancellation,
                node.Interop.Cleanup, node.Interop.Threading);
        }
        foreach (var edge in graph.Edges.OrderBy(static item => item.FromId, StringComparer.Ordinal).ThenBy(static item => item.Order))
            Append("edge", edge.FromId, edge.ToId, edge.Kind, edge.Evidence);
        foreach (var cycle in graph.Cycles) Append("cycle", string.Join("->", cycle));
        foreach (var conflict in graph.Conflicts) Append("conflict", conflict);
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()))
            .Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));

        void Append(params object[] values)
        {
            foreach (var value in values)
            {
                var text = value?.ToString() ?? string.Empty;
                builder.Append(text.Length).Append(':').Append(text);
            }
            builder.Append('\n');
        }
    }

    /// <summary>Throws when a graph's recorded hash does not match its current content.</summary>
    public static void EnsureValid(PowerShellCompilationDependencyGraph graph, string parameterName)
    {
        if (graph is null) throw new ArgumentNullException(parameterName);
        var actual = ComputeSha256(graph);
        if (!actual.Equals(graph.LockSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"PowerShell compilation dependency lock '{parameterName}' has an invalid content hash.");
    }
}
