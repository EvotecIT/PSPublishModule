using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace PowerForge;

internal static partial class PowerShellStrictDependencyClosureVerifier
{
    private static void ReadFacadeMetadata(MetadataReader reader, ManagedAssemblyInspection assembly)
    {
        assembly.IsPureFacade = reader.MethodDefinitions.Count == 0 && reader.ExportedTypes.Count > 0 &&
            reader.TypeDefinitions.All(handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == "<Module>");
        assembly.TypeReferences = reader.TypeReferences
            .Select(handle => ReadReferencedType(reader, handle, assembly.References))
            .Where(static type => type is not null).Cast<ManagedTypeReference>().ToArray();
        assembly.ForwardedTypes = reader.ExportedTypes
            .Select(handle => ReadForwardedType(reader, handle, assembly.References))
            .Where(static type => type is not null).Cast<ManagedTypeReference>().ToArray();
    }

    private static ManagedTypeReference? ReadReferencedType(
        MetadataReader reader, TypeReferenceHandle handle, AssemblyIdentity[] references)
    {
        var name = string.Empty;
        var visited = new HashSet<TypeReferenceHandle>();
        while (visited.Add(handle))
        {
            var type = reader.GetTypeReference(handle);
            name = JoinNestedName(reader.GetString(type.Name), name);
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                handle = (TypeReferenceHandle)type.ResolutionScope;
                continue;
            }
            return type.ResolutionScope.Kind == HandleKind.AssemblyReference
                ? new ManagedTypeReference(ReferencedAssembly(type.ResolutionScope, references),
                    JoinQualifiedName(reader.GetString(type.Namespace), name))
                : null;
        }
        throw new InvalidDataException("Managed type-reference metadata contains a cycle.");
    }

    private static ManagedTypeReference? ReadForwardedType(
        MetadataReader reader, ExportedTypeHandle handle, AssemblyIdentity[] references)
    {
        var name = string.Empty;
        var visited = new HashSet<ExportedTypeHandle>();
        while (visited.Add(handle))
        {
            var type = reader.GetExportedType(handle);
            name = JoinNestedName(reader.GetString(type.Name), name);
            if (type.Implementation.Kind == HandleKind.ExportedType)
            {
                handle = (ExportedTypeHandle)type.Implementation;
                continue;
            }
            return type.IsForwarder && type.Implementation.Kind == HandleKind.AssemblyReference
                ? new ManagedTypeReference(ReferencedAssembly(type.Implementation, references),
                    JoinQualifiedName(reader.GetString(type.Namespace), name))
                : null;
        }
        throw new InvalidDataException("Managed type-forwarding metadata contains a cycle.");
    }

    private static AssemblyIdentity ReferencedAssembly(EntityHandle handle, AssemblyIdentity[] references)
    {
        var index = MetadataTokens.GetRowNumber((AssemblyReferenceHandle)handle) - 1;
        if (index < 0 || index >= references.Length)
            throw new InvalidDataException("Managed type metadata references an invalid assembly row.");
        return references[index];
    }

    private static string JoinNestedName(string parent, string child)
        => child.Length == 0 ? parent : parent + "+" + child;

    private static string JoinQualifiedName(string ns, string name)
        => ns.Length == 0 ? name : ns + "." + name;

    private static void VerifyUsedFacadeReferences(
        IReadOnlyCollection<ManagedAssemblyInspection> assemblies,
        ISet<string> targetRuntimeAssemblies,
        IReadOnlyDictionary<string, Version> compatibleSignedRuntimeAssemblies)
    {
        foreach (var source in assemblies)
        foreach (var use in source.TypeReferences.Concat(source.IsReviewedRuntimePack && source.IsPureFacade
                     ? Array.Empty<ManagedTypeReference>() : source.ForwardedTypes))
        {
            var current = use;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var followedForwarder = false;
            while (true)
            {
                if (!visited.Add(current.Assembly.StableKey + "\0" + current.Name))
                    throw new InvalidOperationException($"Strict runtime-free type '{use.Name}' has a cyclic forwarding chain.");
                var destination = assemblies.FirstOrDefault(assembly => assembly.Identity.StableKey.Equals(
                    current.Assembly.StableKey, StringComparison.OrdinalIgnoreCase));
                if (destination is null && IsResolvedRuntimeFacadeReference(current.Assembly, assemblies,
                        targetRuntimeAssemblies, compatibleSignedRuntimeAssemblies))
                    destination = assemblies.FirstOrDefault(assembly =>
                        assembly.Identity.Name.Equals(current.Assembly.Name, StringComparison.OrdinalIgnoreCase) &&
                        assembly.Identity.PublicKeyToken.Equals(current.Assembly.PublicKeyToken, StringComparison.OrdinalIgnoreCase) &&
                        assembly.Identity.Culture.Equals(current.Assembly.Culture, StringComparison.OrdinalIgnoreCase) &&
                        assembly.Identity.ContentType.Equals(current.Assembly.ContentType, StringComparison.OrdinalIgnoreCase) &&
                        assembly.Identity.Version >= current.Assembly.Version);
                if (destination is null)
                {
                    if (followedForwarder && !targetRuntimeAssemblies.Contains(current.Assembly.StableKey) &&
                        !IsResolvedRuntimeFacadeReference(current.Assembly, assemblies, targetRuntimeAssemblies, compatibleSignedRuntimeAssemblies))
                        throw new InvalidOperationException($"Strict runtime-free managed dependency '{source.DisplayPath}' uses forwarded type '{use.Name}' whose destination '{current.Assembly.DisplayName}' is missing.");
                    break;
                }
                var forwarding = destination.ForwardedTypes.FirstOrDefault(type => type.Name.Equals(current.Name, StringComparison.Ordinal));
                if (forwarding is null) break;
                current = forwarding;
                followedForwarder = true;
            }
        }
    }

    private sealed class ManagedTypeReference
    {
        internal ManagedTypeReference(AssemblyIdentity assembly, string name) { Assembly = assembly; Name = name; }
        internal AssemblyIdentity Assembly { get; }
        internal string Name { get; }
    }
}
