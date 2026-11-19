using System.Management.Automation.Language;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using TypeName = System.Management.Automation.Language.TypeName;
using TypeAttributes = System.Reflection.TypeAttributes;
using MethodAttributes = System.Reflection.MethodAttributes;

namespace PowerForge;

/// <summary>Metadata-only identities from locked module dependencies. Never loads dependency code.</summary>
internal sealed class PowerShellNativeDependencyTypes
{
    internal static readonly PowerShellNativeDependencyTypes Empty = new(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    private readonly HashSet<string> _names;
    private readonly HashSet<string> _aliases;
    private readonly HashSet<string> _moduleLocalClasses;

    private PowerShellNativeDependencyTypes(HashSet<string> names, HashSet<string>? aliases = null,
        HashSet<string>? moduleLocalClasses = null)
    {
        _names = names;
        _aliases = aliases ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _moduleLocalClasses = moduleLocalClasses ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Identifies a locked authored class also loaded by the module's contained loader.</summary>
    internal bool IsModuleLocalClass(ITypeName name, PowerShellCompilationCapability capabilities)
        => name is TypeName && _moduleLocalClasses.Contains(name.FullName) && Qualifies(name, capabilities);

    internal bool RequiresDeferredBinding(FunctionDefinitionAst function)
        => PowerShellParameterSyntax.GetParameters(function.Body).Any(parameter =>
               parameter.Attributes.OfType<TypeConstraintAst>().Any(constraint => ContainsAlias(constraint.TypeName))) ||
           function.Body.Find(node => node is TypeConstraintAst constraint && ContainsAlias(constraint.TypeName),
               searchNestedScriptBlocks: false) is not null;

    private bool ContainsAlias(ITypeName name)
        => _aliases.Contains(name.FullName) || name is ArrayTypeName array && ContainsAlias(array.ElementType) ||
           name is GenericTypeName generic && generic.GenericArguments.Any(ContainsAlias);

    /// <summary>Only the active native binder may resolve these authored names; no CLR reference is inferred.</summary>
    internal bool Qualifies(ITypeName name, PowerShellCompilationCapability capabilities)
        => capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) &&
           capabilities.HasFlag(PowerShellCompilationCapability.PowerShellHostTypes) &&
           IsClosedShape(name, capabilities, out var containsDependency) && containsDependency;

    // These unconstrained containers share the ordinary generated type policy. Their
    // element identities stay with the host; we never construct a foreign CLR Type.
    private bool IsClosedShape(ITypeName name, PowerShellCompilationCapability capabilities, out bool containsDependency)
    {
        containsDependency = false;
        if (!string.IsNullOrEmpty(name.AssemblyName)) return false;
        if (name is ArrayTypeName array)
            return array.Rank == 1 && array.ElementType is not ArrayTypeName &&
                   IsClosedShape(array.ElementType, capabilities, out containsDependency);
        if (name is GenericTypeName generic)
        {
            if (!string.IsNullOrEmpty(generic.TypeName.AssemblyName)) return false;
            var arity = generic.GenericArguments.Count;
            var authoredDefinition = generic.TypeName.FullName;
            var tick = authoredDefinition.LastIndexOf('`');
            var metadataName = tick < 0 ? authoredDefinition + "`" + arity : authoredDefinition;
            var deliveredDefinition = _names.Contains(metadataName) &&
                                      (tick < 0 || authoredDefinition.Substring(tick + 1) == arity.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var definition = new[] { typeof(List<>), typeof(Dictionary<,>), typeof(HashSet<>) }
                .SingleOrDefault(candidate => candidate.GetGenericArguments().Length == generic.GenericArguments.Count &&
                    (candidate.FullName!.Split('`')[0].Equals(generic.TypeName.FullName, StringComparison.OrdinalIgnoreCase) ||
                     candidate.FullName!.Split('`')[0].Substring("System.".Length).Equals(generic.TypeName.FullName, StringComparison.OrdinalIgnoreCase)));
            if (!deliveredDefinition &&
                (definition is null || !PowerShellGeneratedTypePolicy.IsSupportedContainerDefinition(definition, "net472"))) return false;
            containsDependency = deliveredDefinition;
            foreach (var argument in generic.GenericArguments)
            {
                if (!IsClosedShape(argument, capabilities, out var dependency)) return false;
                containsDependency |= dependency;
            }
            return true;
        }
        if (name is not TypeName || name.FullName.IndexOf('`') >= 0) return false;
        if (_names.Contains(name.FullName)) return containsDependency = true;
        return name.GetReflectionType() is { } known &&
               PowerShellCompilationParameterTypePolicy.CanUseInMethod(known, "net472", capabilities);
    }

    internal bool IsUsedBy(FunctionDefinitionAst function, PowerShellCompilationCapability capabilities)
        => PowerShellParameterSyntax.GetParameters(function.Body).Any(parameter =>
               parameter.Attributes.OfType<TypeConstraintAst>().Any(constraint => Qualifies(constraint.TypeName, capabilities))) ||
           function.Body.Find(node => node is TypeExpressionAst expression && Qualifies(expression.TypeName, capabilities) ||
               node is TypeConstraintAst constraint && Qualifies(constraint.TypeName, capabilities), searchNestedScriptBlocks: false) is not null;

    /// <summary>Reads delivered assembly types and manifest-loaded classes only after their bytes are locked.</summary>
    internal static PowerShellNativeDependencyTypes Create(string? manifestPath,
        IEnumerable<PowerShellCompilationDependency> dependencies, PowerShellCompilationDependencyGraph graph,
        string? moduleRoot = null)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath) ||
            graph.Conflicts.Length != 0 || graph.Cycles.Length != 0) return Empty;
        var root = Path.GetDirectoryName(Path.GetFullPath(manifestPath!))!;
        var graphRoot = Path.GetFullPath(moduleRoot ?? root);
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidates = dependencies.Where(dependency =>
            dependency.Kind == PowerShellCompilationDependencyKind.ManagedAssembly && dependency.Exists &&
            (dependency.Disposition is PowerShellCompilationDependencyDisposition.CopiedAdjacent or PowerShellCompilationDependencyDisposition.EmbeddedAndExtracted) &&
            dependency.SourcePath is not null &&
            PowerShellCompilationPathSafety.PathStartsWith(Path.GetFullPath(dependency.SourcePath), rootPrefix))
            .Select(dependency => Path.GetFullPath(dependency.SourcePath!)).ToArray();
        var borrowed = graph.Nodes.Where(static node => node.Exists &&
                node.Disposition == PowerShellCompilationDependencyGraphDisposition.External &&
                node.Identity.Provenance.Equals(PowerShellWindowsModuleMetadata.AssemblyProvenance, StringComparison.Ordinal))
            .Select(node => Path.GetFullPath(node.Identity.Source)).ToHashSet(PowerShellCompilationPathSafety.PathComparer);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in candidates.Concat(borrowed).Distinct(PowerShellCompilationPathSafety.PathComparer))
        {
            var isBorrowed = borrowed.Contains(path);
            if (isBorrowed)
                PowerShellWindowsModuleMetadata.RequireTrustedAssemblyPath(path);
            else
            {
                PowerShellCompilationPathSafety.EnsureContained(root, path, "Native type dependency must stay inside the root module.");
                PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(path, "Native type dependency must not traverse links.");
            }
            var nodes = graph.Nodes.Where(node => node.Exists &&
                node.Disposition != PowerShellCompilationDependencyGraphDisposition.Rejected &&
                PowerShellCompilationPathSafety.PathEquals(Path.GetFullPath(Path.Combine(graphRoot, node.Identity.Source)), path)).ToArray();
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
            if (!reader.IsAssembly || !isBorrowed && reader.MethodDefinitions.Any(handle =>
                    (reader.GetMethodDefinition(handle).Attributes & MethodAttributes.PinvokeImpl) != 0)) continue;
            var assembly = reader.GetAssemblyDefinition();
            var owner = reader.GetString(assembly.Name) + "|" + assembly.Version + "|" +
                        BitConverter.ToString(reader.GetBlobBytes(assembly.PublicKey)).Replace("-", string.Empty).ToLowerInvariant() + "|" +
                        (assembly.Culture.IsNil ? string.Empty : reader.GetString(assembly.Culture));
            foreach (var handle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(handle);
                if ((type.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public || !type.GetDeclaringType().IsNil) continue;
                var typeName = reader.GetString(type.Name);
                var arity = type.GetGenericParameters().Count;
                if (arity != 0 && !typeName.EndsWith("`" + arity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        StringComparison.Ordinal)) continue;
                var typeNamespace = reader.GetString(type.Namespace);
                var name = (typeNamespace.Length == 0 ? string.Empty : typeNamespace + ".") + typeName;
                if (!names.Add(name) && !owners[name].Equals(owner, StringComparison.OrdinalIgnoreCase)) ambiguous.Add(name);
                else owners[name] = owner;
            }
        }
        var declaredScripts = (ModuleManifestValueReader.ReadTopLevelLiteralStringOrArrayOrThrow(manifestPath!, "ScriptsToProcess") ?? Array.Empty<string>())
            .Where(static value => !Path.IsPathRooted(value) && value.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            .Select(value => Path.GetFullPath(Path.Combine(root, PowerShellCompiledModuleManifest.NormalizeManifestRelativePath(value))))
            .ToArray();
        var moduleLocalSources = declaredScripts.Length == 0
            ? new HashSet<string>(PowerShellCompilationPathSafety.PathComparer)
            : ReadModuleLocalSources(manifestPath!, root);
        var moduleLocalClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in declaredScripts)
        {
            PowerShellCompilationPathSafety.EnsureContained(root, path, "Authored class declaration must stay inside the root module.");
            PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(path, "Authored class declaration must not traverse links.");
            var delivered = dependencies.Where(dependency => dependency.Discovery == PowerShellCompilationDependencyDiscovery.ScriptsToProcess &&
                dependency.Kind == PowerShellCompilationDependencyKind.PowerShellSource && dependency.Exists &&
                dependency.Disposition == PowerShellCompilationDependencyDisposition.CopiedAdjacent &&
                dependency.SourcePath is not null && PowerShellCompilationPathSafety.PathEquals(dependency.SourcePath, path)).ToArray();
            if (delivered.Length != 1) continue;
            var nodes = graph.Nodes.Where(node => node.Exists &&
                node.Disposition != PowerShellCompilationDependencyGraphDisposition.Rejected &&
                PowerShellCompilationPathSafety.PathEquals(Path.GetFullPath(Path.Combine(graphRoot, node.Identity.Source)), path)).ToArray();
            if (nodes.Length != 1 || string.IsNullOrWhiteSpace(nodes[0].Identity.Sha256)) continue;
            var lockedBytes = File.ReadAllBytes(path);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(lockedBytes)).Replace("-", string.Empty).ToLowerInvariant();
            if (!hash.Equals(nodes[0].Identity.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Authored class declaration changed after dependency locking.");
            using var source = new StreamReader(new MemoryStream(lockedBytes, writable: false), Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            var syntax = Parser.ParseInput(source.ReadToEnd(), out _, out var errors);
            if (errors.Length != 0) continue;
            foreach (var definition in syntax.FindAll(static node => node is TypeDefinitionAst { IsClass: true },
                         searchNestedScriptBlocks: false).OfType<TypeDefinitionAst>()
                         .Where(definition => ReferenceEquals(definition.Parent, syntax.EndBlock)))
                if (!names.Add(definition.Name)) ambiguous.Add(definition.Name);
                else if (moduleLocalSources.Contains(path)) moduleLocalClasses.Add(definition.Name);
        }
        names.ExceptWith(ambiguous);
        moduleLocalClasses.ExceptWith(ambiguous);
        var originalNames = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        PowerShellNativeTypeAliases.Add(root, graphRoot, dependencies, graph, names);
        return new PowerShellNativeDependencyTypes(names,
            new HashSet<string>(names.Where(name => !originalNames.Contains(name)), StringComparer.OrdinalIgnoreCase),
            moduleLocalClasses);
    }

    private static HashSet<string> ReadModuleLocalSources(string manifestPath, string root)
    {
        var relativeEntry = PowerShellCompiledModuleManifest.ReadRootModule(manifestPath);
        var paths = new HashSet<string>(PowerShellCompilationPathSafety.PathComparer);
        if (relativeEntry is null || !relativeEntry.EndsWith(".psm1", StringComparison.OrdinalIgnoreCase) ||
            Path.IsPathRooted(relativeEntry)) return paths;
        var entry = Path.GetFullPath(Path.Combine(root, PowerShellCompiledModuleManifest.NormalizeManifestRelativePath(relativeEntry)));
        PowerShellCompilationPathSafety.EnsureContained(root, entry, "Module class loader must stay inside the root module.");
        PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(entry, "Module class loader must not traverse links.");
        if (File.Exists(entry)) paths.UnionWith(PowerShellConventionalModuleSourceDiscovery.Analyze(entry).SourcePaths);
        return paths;
    }

}
