using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private static bool SameReflectedMember(MemberInfo left, MemberInfo right) =>
        left.Module == right.Module && left.MetadataToken == right.MetadataToken;

    private static void ResolveImplicitInheritDoc(Assembly assembly, Dictionary<string, XElement> members, string assemblyPath,
        Dictionary<string, XElement> frameworkFallbacks, IReadOnlyList<string> sourceMemberIds, List<string> warnings)
    {
        var reflected = new Dictionary<string, MemberInfo>(StringComparer.Ordinal);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var type in GetExportedTypesSafe(assembly))
        {
            if (type is null) continue;
            reflected[DocumentationId(type)] = type;
            foreach (var member in type.GetMembers(flags))
            {
                if (member is MethodInfo or PropertyInfo or EventInfo)
                    reflected[DocumentationId(member)] = member;
            }
        }
        IReadOnlyList<string>? referenceInputs = null;
        var loadedReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Imported records may themselves inherit documentation; process reachable
        // records with their reflected metadata before the model follows their cref.
        var pendingRecords = new Queue<KeyValuePair<string, XElement>>(sourceMemberIds.Select(id => new KeyValuePair<string, XElement>(id, members[id])));
        var resolvedRecords = new HashSet<string>(StringComparer.Ordinal);
        while (pendingRecords.TryDequeue(out var pair))
        {
            var inheritDoc = pair.Value.Element("inheritdoc");
            if (inheritDoc is null) continue;
            var explicitTarget = inheritDoc.Attribute("cref")?.Value;
            var implicitTarget = string.IsNullOrWhiteSpace(explicitTarget);
            if (!reflected.TryGetValue(pair.Key, out var member)) continue;
            if (!resolvedRecords.Add(pair.Key)) continue;
            var candidates = InheritedDocumentationMembers(member).ToArray();
            var inheritedOwnerNames = candidates.Select(candidate => candidate is Type candidateType
                    ? candidateType.Assembly.GetName().Name : candidate.DeclaringType!.Assembly.GetName().Name)
                .OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            string? target = null;
            var missingLibraryContract = false;
            var targets = implicitTarget ? candidates.Select(DocumentationId).ToArray() : new[] { explicitTarget! };
            foreach (var candidateId in targets)
            {
                var candidate = candidates.FirstOrDefault(candidate => DocumentationId(candidate) == candidateId)
                    ?? (implicitTarget ? null : FindLoadedInheritDocMember(candidateId, assembly) ?? FrameworkInheritDocMember(candidateId));
                var declaring = candidate as Type ?? candidate?.DeclaringType;
                if (implicitTarget && missingLibraryContract && declaring?.Assembly == typeof(object).Assembly)
                    continue;
                if (!members.ContainsKey(candidateId))
                {
                    referenceInputs ??= WebApiDocumentationInputs.Discover(assemblyPath);
                    var ownerName = declaring?.Assembly.GetName().Name;
                    var assemblyDirectory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
                    var candidateInputs = referenceInputs.Where(path =>
                        !string.Equals(Path.GetDirectoryName(path), assemblyDirectory, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Path.GetFileNameWithoutExtension(path), ownerName, StringComparison.OrdinalIgnoreCase) ||
                        inheritedOwnerNames.Contains(Path.GetFileNameWithoutExtension(path)));
                    foreach (var path in candidateInputs.OrderBy(path => ReferenceDocumentationPriority(path, ownerName, declaring?.Namespace)))
                    {
                        if (!loadedReferences.Add(path)) continue;
                        var document = LoadXmlDocumentation(path, warnings);
                        foreach (var inherited in document.Root!.Element("members")!.Elements("member"))
                        {
                            if (inherited.Attribute("name")?.Value is not { Length: > 0 } id) continue;
                            members.TryAdd(id, inherited);
                        }
                        if (members.ContainsKey(candidateId)) break;
                    }
                }
                if (!members.ContainsKey(candidateId))
                {
                    if (implicitTarget && declaring?.Assembly != typeof(object).Assembly)
                        missingLibraryContract = true;
                    continue;
                }
                target = candidateId;
                if (candidate is not null) reflected.TryAdd(candidateId, candidate);
                if (reflected.ContainsKey(candidateId) && members[candidateId].Element("inheritdoc") is not null)
                    pendingRecords.Enqueue(new(candidateId, members[candidateId]));
                break;
            }
            // Prefer real XML documentation. Only synthesize the nearest well-known framework contract;
            // a missing library contract must retain the unresolved-inheritdoc diagnostic.
            var fallbackMember = implicitTarget ? candidates.FirstOrDefault() : FrameworkInheritDocMember(explicitTarget!);
            if (target is null && fallbackMember is { } nearest &&
                CreateFrameworkInheritDoc(nearest) is { } frameworkDocumentation)
            {
                target = DocumentationId(nearest);
                frameworkFallbacks.TryAdd(target, frameworkDocumentation);
            }
            if (implicitTarget && target is not null) inheritDoc.SetAttributeValue("cref", target);
            else if (implicitTarget) warnings.Add($"Implicit inheritdoc could not be resolved for {pair.Key}; supply the inherited XML documentation or an explicit cref.");
        }
    }

    private static MemberInfo? FindLoadedInheritDocMember(string documentationId, Assembly sourceAssembly)
    {
        var loadContext = AssemblyLoadContext.GetLoadContext(sourceAssembly);
        var assemblies = (loadContext?.Assemblies ?? Array.Empty<Assembly>())
            .Concat(AppDomain.CurrentDomain.GetAssemblies().Where(candidate => AssemblyLoadContext.GetLoadContext(candidate) == AssemblyLoadContext.Default))
            .Distinct()
            .OrderBy(candidate => candidate == sourceAssembly ? 0 : 1);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var candidateAssembly in assemblies)
        foreach (var type in GetExportedTypesSafe(candidateAssembly))
        {
            if (type is null) continue;
            if (DocumentationId(type) == documentationId) return type;
            foreach (var member in type.GetMembers(flags))
                if (member is (MethodInfo or PropertyInfo or EventInfo) && DocumentationId(member) == documentationId)
                    return member;
        }
        return null;
    }

    private static int ReferenceDocumentationPriority(string path, string? ownerName, string? typeNamespace)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.Equals(ownerName, StringComparison.OrdinalIgnoreCase)) return 0;
        if (typeNamespace is not null && (typeNamespace.Equals(name, StringComparison.Ordinal) || typeNamespace.StartsWith(name + ".", StringComparison.Ordinal))) return 1;
        return name.Equals("System.Runtime", StringComparison.OrdinalIgnoreCase) ? 2 : 3;
    }

    private static string DocumentationId(MemberInfo member)
    {
        if (member is Type type)
            return "T:" + (type.IsConstructedGenericType ? type.GetGenericTypeDefinition() : type).FullName!.Replace('+', '.');
        if (member.DeclaringType!.IsConstructedGenericType)
            member = member.DeclaringType.GetGenericTypeDefinition().GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .First(candidate => SameReflectedMember(candidate, member));
        var declaring = member.DeclaringType!.FullName!.Replace('+', '.');
        return member switch
        {
            MethodInfo method => "M:" + declaring + "." + BuildDocumentationMethodSignature(method),
            PropertyInfo property => "P:" + declaring + "." + BuildDocumentationPropertySignature(property),
            EventInfo => "E:" + declaring + "." + member.Name,
            _ => throw new ArgumentException("Unsupported documentation member.", nameof(member))
        };
    }

    private static IEnumerable<MemberInfo> InheritedDocumentationMembers(MemberInfo member)
    {
        if (member is Type type)
        {
            for (var parent = type.BaseType; parent is not null; parent = parent.BaseType) yield return parent;
            foreach (var contract in type.GetInterfaces().OrderBy(static contract => contract.FullName, StringComparer.Ordinal)) yield return contract;
            yield break;
        }
        var accessor = member switch
        {
            MethodInfo method => method,
            PropertyInfo property => GetMostVisibleAccessor(property.GetMethod, property.SetMethod),
            EventInfo evt => evt.AddMethod ?? evt.RemoveMethod,
            _ => null
        };
        if (accessor is null) yield break;
        var declaringType = member.DeclaringType!;
        if (accessor.IsVirtual)
        {
            var definition = accessor.GetBaseDefinition();
            for (var parent = declaringType.BaseType; parent is not null; parent = parent.BaseType)
            {
                foreach (var candidate in parent.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (SameReflectedMember(candidate.GetBaseDefinition(), definition))
                        yield return DocumentationMemberForAccessor(candidate, member);
                }
            }
        }
        foreach (var contract in declaringType.GetInterfaces().OrderBy(static contract => contract.FullName, StringComparer.Ordinal))
        {
            if (declaringType.IsInterface)
            {
                foreach (var candidate in contract.GetMethods().Where(candidate => BuildDocumentationMethodSignature(candidate) == BuildDocumentationMethodSignature(accessor)))
                    yield return DocumentationMemberForAccessor(candidate, member);
                continue;
            }
            var map = declaringType.GetInterfaceMap(contract);
            for (var index = 0; index < map.TargetMethods.Length; index++)
                if (SameReflectedMember(map.TargetMethods[index], accessor))
                    yield return DocumentationMemberForAccessor(map.InterfaceMethods[index], member);
        }
    }

    private static MemberInfo DocumentationMemberForAccessor(MethodInfo accessor, MemberInfo original)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        if (original is PropertyInfo)
            return accessor.DeclaringType!.GetProperties(flags).First(property =>
                (property.GetMethod is { } getter && SameReflectedMember(getter, accessor)) ||
                (property.SetMethod is { } setter && SameReflectedMember(setter, accessor)));
        if (original is EventInfo)
            return accessor.DeclaringType!.GetEvents(flags).First(evt =>
                (evt.AddMethod is { } add && SameReflectedMember(add, accessor)) ||
                (evt.RemoveMethod is { } remove && SameReflectedMember(remove, accessor)));
        return accessor;
    }
}
