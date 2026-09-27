using System.Management.Automation.Language;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using TypeName = System.Management.Automation.Language.TypeName;
using TypeAttributes = System.Reflection.TypeAttributes;
using MethodAttributes = System.Reflection.MethodAttributes;

namespace PowerForge;

/// <summary>Metadata-only identities from contained, delivered root RequiredAssemblies. Never loads dependency code.</summary>
internal sealed class PowerShellNativeDependencyTypes
{
    internal static readonly PowerShellNativeDependencyTypes Empty = new(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    private readonly HashSet<string> _names;

    private PowerShellNativeDependencyTypes(HashSet<string> names) => _names = names;

    /// <summary>Only the active native binder may resolve these authored names; no CLR reference is inferred.</summary>
    internal bool Qualifies(ITypeName name, PowerShellCompilationCapability capabilities)
        => capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) &&
           capabilities.HasFlag(PowerShellCompilationCapability.PowerShellHostTypes) &&
           name is TypeName && string.IsNullOrEmpty(name.AssemblyName) && _names.Contains(name.FullName);

    internal bool IsUsedBy(FunctionDefinitionAst function, PowerShellCompilationCapability capabilities)
        => function.Body.Find(node => node is TypeExpressionAst expression && Qualifies(expression.TypeName, capabilities) ||
            node is TypeConstraintAst constraint && Qualifies(constraint.TypeName, capabilities), searchNestedScriptBlocks: false) is not null;

    /// <summary>Reads public nongeneric types only after the ordinary dependency graph has locked their bytes.</summary>
    internal static PowerShellNativeDependencyTypes Create(string? manifestPath,
        IEnumerable<PowerShellCompilationDependency> dependencies, PowerShellCompilationDependencyGraph graph,
        string? moduleRoot = null)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath) ||
            graph.Conflicts.Length != 0 || graph.Cycles.Length != 0) return Empty;
        var root = Path.GetDirectoryName(Path.GetFullPath(manifestPath!))!;
        var graphRoot = Path.GetFullPath(moduleRoot ?? root);
        var declared = (ModuleManifestValueReader.ReadTopLevelLiteralStringOrArrayOrThrow(manifestPath!, "RequiredAssemblies") ?? Array.Empty<string>())
            .Where(static value => !Path.IsPathRooted(value) && value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(value => Path.GetFullPath(Path.Combine(root, PowerShellCompiledModuleManifest.NormalizeManifestRelativePath(value))))
            .ToHashSet(PowerShellCompilationPathSafety.PathComparer);
        var candidates = dependencies.Where(dependency => dependency.Discovery == PowerShellCompilationDependencyDiscovery.RequiredAssemblies &&
            dependency.Kind == PowerShellCompilationDependencyKind.ManagedAssembly && dependency.Exists &&
            (dependency.Disposition is PowerShellCompilationDependencyDisposition.CopiedAdjacent or PowerShellCompilationDependencyDisposition.EmbeddedAndExtracted) &&
            dependency.SourcePath is not null && declared.Contains(Path.GetFullPath(dependency.SourcePath))).ToArray();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dependency in candidates)
        {
            var path = Path.GetFullPath(dependency.SourcePath!);
            PowerShellCompilationPathSafety.EnsureContained(root, path, "Native type dependency must stay inside the root module.");
            PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(path, "Native type dependency must not traverse links.");
            var nodes = graph.Nodes.Where(node => node.Exists &&
                node.Disposition != PowerShellCompilationDependencyGraphDisposition.Rejected &&
                PowerShellCompilationPathSafety.PathEquals(Path.Combine(graphRoot, node.Identity.Source), path)).ToArray();
            if (nodes.Length != 1 || string.IsNullOrWhiteSpace(nodes[0].Identity.Sha256)) continue;
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            if (!hash.Equals(nodes[0].Identity.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Native type dependency changed after dependency locking.");
            stream.Position = 0;
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) continue;
            var reader = pe.GetMetadataReader();
            if (!reader.IsAssembly || reader.MethodDefinitions.Any(handle =>
                    (reader.GetMethodDefinition(handle).Attributes & MethodAttributes.PinvokeImpl) != 0)) continue;
            foreach (var handle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(handle);
                if ((type.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public ||
                    type.GetGenericParameters().Count != 0 || !type.GetDeclaringType().IsNil) continue;
                var typeNamespace = reader.GetString(type.Namespace);
                var name = (typeNamespace.Length == 0 ? string.Empty : typeNamespace + ".") + reader.GetString(type.Name);
                if (!names.Add(name)) ambiguous.Add(name);
            }
        }
        names.ExceptWith(ambiguous);
        return new PowerShellNativeDependencyTypes(names);
    }
}
