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

/// <summary>Type names supplied by locked dependencies and authored module declarations.</summary>
/// <remarks>PowerShell resolves the actual types in the active module.</remarks>
internal sealed class PowerShellNativeDependencyTypes
{
    internal static readonly PowerShellNativeDependencyTypes Empty = new(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    private readonly HashSet<string> _names;
    private readonly HashSet<string> _aliases;
    private readonly HashSet<string> _moduleLocalClasses;
    private readonly HashSet<string> _runtimeLoadedTypes;

    private PowerShellNativeDependencyTypes(HashSet<string> names, HashSet<string>? aliases = null,
        HashSet<string>? moduleLocalClasses = null, HashSet<string>? runtimeLoadedTypes = null)
    {
        _names = names;
        _aliases = aliases ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _moduleLocalClasses = moduleLocalClasses ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _runtimeLoadedTypes = runtimeLoadedTypes ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    internal PowerShellNativeDependencyTypes WithRuntimeLoadedTypes(HashSet<string> names)
        => names.Count == 0 ? this : new PowerShellNativeDependencyTypes(_names, _aliases, _moduleLocalClasses,
            _runtimeLoadedTypes.Count == 0 ? names : _runtimeLoadedTypes.Concat(names).ToHashSet(StringComparer.OrdinalIgnoreCase));

    internal PowerShellNativeDependencyTypes WithModuleDeclaredTypes(IEnumerable<string> names)
        => new(_names.Concat(names).ToHashSet(StringComparer.OrdinalIgnoreCase), _aliases, _moduleLocalClasses, _runtimeLoadedTypes);

    /// <summary>Identifies a locked authored class also loaded by the module's contained loader.</summary>
    internal bool IsModuleLocalClass(ITypeName name, PowerShellCompilationCapability capabilities)
        => name is TypeName && _moduleLocalClasses.Contains(name.FullName) && Qualifies(name, capabilities);

    internal bool RequiresDeferredBinding(FunctionDefinitionAst function)
        => PowerShellParameterSyntax.GetParameters(function.Body).Any(parameter =>
               parameter.Attributes.OfType<TypeConstraintAst>().Any(constraint => ContainsDeferredType(constraint.TypeName))) ||
           function.Body.Find(node => node is AssignmentStatementAst { Left: AttributedExpressionAst target } &&
               target.Find(type => type is TypeConstraintAst constraint && ContainsDeferredType(constraint.TypeName),
                   searchNestedScriptBlocks: false) is not null,
               searchNestedScriptBlocks: false) is not null;

    private bool ContainsDeferredType(ITypeName name)
        => _aliases.Contains(name.FullName) || _runtimeLoadedTypes.Contains(name.FullName) ||
           name is ArrayTypeName array && ContainsDeferredType(array.ElementType) ||
           name is GenericTypeName generic && generic.GenericArguments.Any(ContainsDeferredType);

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
        if (_names.Contains(name.FullName) || _runtimeLoadedTypes.Contains(name.FullName)) return containsDependency = true;
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
        string? moduleRoot = null, PowerShellSourceSnapshot? snapshot = null)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath) ||
            graph.Conflicts.Length != 0 || graph.Cycles.Length != 0) return Empty;
        var root = Path.GetDirectoryName(Path.GetFullPath(manifestPath!))!;
        var graphRoot = Path.GetFullPath(moduleRoot ?? root);
        snapshot ??= new PowerShellSourceSnapshot(graphRoot, null);
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
            .ToDictionary(node => PowerShellWindowsModuleMetadata.ResolvePath(node.Identity.Source),
                PowerShellCompilationPathSafety.PathComparer);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in candidates.Concat(borrowed.Keys).Distinct(PowerShellCompilationPathSafety.PathComparer))
        {
            var isBorrowed = borrowed.TryGetValue(path, out var hostAssembly);
            if (isBorrowed)
                PowerShellWindowsModuleMetadata.RequireTrustedAssemblyPath(path);
            else
            {
                PowerShellCompilationPathSafety.EnsureContained(root, path, "Native type dependency must stay inside the root module.");
                PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(path, "Native type dependency must not traverse links.");
            }
            var nodes = isBorrowed ? new[] { hostAssembly! } : graph.Nodes.Where(node => node.Exists &&
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
            if (!pe.HasMetadata || isBorrowed && !PowerShellWindowsModuleMetadata.HasCompatibleArchitecture(pe)) continue;
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
        var moduleLocalSources = ReadModuleLocalSources(manifestPath!, root, declaredScripts.Length != 0, snapshot,
            out var literalDotSources);
        var moduleLocalClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in declaredScripts.Concat(literalDotSources).Distinct(PowerShellCompilationPathSafety.PathComparer))
        {
            PowerShellCompilationPathSafety.EnsureContained(root, path, "Authored class declaration must stay inside the root module.");
            PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(path, "Authored class declaration must not traverse links.");
            var delivered = dependencies.Where(dependency =>
                (dependency.Discovery == PowerShellCompilationDependencyDiscovery.ScriptsToProcess || literalDotSources.Contains(path)) &&
                dependency.Kind == PowerShellCompilationDependencyKind.PowerShellSource && dependency.Exists &&
                dependency.Disposition == PowerShellCompilationDependencyDisposition.CopiedAdjacent &&
                dependency.SourcePath is not null && PowerShellCompilationPathSafety.PathEquals(dependency.SourcePath, path)).ToArray();
            if (delivered.Length != 1) continue;
            var nodes = graph.Nodes.Where(node => node.Exists &&
                node.Disposition != PowerShellCompilationDependencyGraphDisposition.Rejected &&
                PowerShellCompilationPathSafety.PathEquals(Path.GetFullPath(Path.Combine(graphRoot, node.Identity.Source)), path)).ToArray();
            if (nodes.Length != 1 || string.IsNullOrWhiteSpace(nodes[0].Identity.Sha256)) continue;
            var document = snapshot.Get(path);
            if (!string.Equals(snapshot.GetCapturedSha256(path), nodes[0].Identity.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Authored class declaration changed after dependency locking.");
            if (document.Errors.Length != 0) continue;
            var syntax = document.SyntaxRoot;
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

    private static HashSet<string> ReadModuleLocalSources(string manifestPath, string root, bool includeConventional,
        PowerShellSourceSnapshot snapshot, out HashSet<string> literalDotSources)
    {
        var relativeEntry = PowerShellCompiledModuleManifest.ReadRootModule(manifestPath);
        var paths = new HashSet<string>(PowerShellCompilationPathSafety.PathComparer);
        literalDotSources = new HashSet<string>(PowerShellCompilationPathSafety.PathComparer);
        if (relativeEntry is null || !relativeEntry.EndsWith(".psm1", StringComparison.OrdinalIgnoreCase) ||
            Path.IsPathRooted(relativeEntry)) return paths;
        var entry = Path.GetFullPath(Path.Combine(root, PowerShellCompiledModuleManifest.NormalizeManifestRelativePath(relativeEntry)));
        PowerShellCompilationPathSafety.EnsureContained(root, entry, "Module class loader must stay inside the root module.");
        PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(entry, "Module class loader must not traverse links.");
        if (!File.Exists(entry)) return paths;
        if (includeConventional) paths.UnionWith(PowerShellConventionalModuleSourceDiscovery.Analyze(entry, snapshot: snapshot).SourcePaths);
        var document = snapshot.Get(entry);
        if (document.Errors.Length != 0) return paths;
        var ast = document.SyntaxRoot;
        foreach (var command in ast.FindAll(static node => node is CommandAst { InvocationOperator: TokenKind.Dot },
                     searchNestedScriptBlocks: false).OfType<CommandAst>())
        {
            if (!PowerShellCompilationSourcePathPlanner.TryPlan(ast, command, allowDerivedPaths: false, out var fact) ||
                !fact!.RelativePath.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) ||
                fact.RelativePath.IndexOf(':') >= 0 || fact.RelativePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                continue;
            var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(entry)!,
                PowerShellCompiledModuleManifest.NormalizeManifestRelativePath(fact.RelativePath)));
            if (PowerShellCompilationPathSafety.PathStartsWith(path, root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar))
                literalDotSources.Add(path);
        }
        paths.UnionWith(literalDotSources);
        return paths;
    }

}
