using System.Reflection;
using System.Xml.Linq;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private static bool SameReflectedMember(MemberInfo left, MemberInfo right) =>
        left.Module == right.Module && left.MetadataToken == right.MetadataToken;

    private static void ResolveImplicitInheritDoc(Assembly assembly, Dictionary<string, XElement> members, WebApiDocsOptions options, List<string> warnings)
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
        foreach (var pair in members.ToArray())
        {
            var inheritDoc = pair.Value.Element("inheritdoc");
            if (inheritDoc is null) continue;
            var explicitTarget = inheritDoc.Attribute("cref")?.Value;
            var implicitTarget = string.IsNullOrWhiteSpace(explicitTarget);
            if (!reflected.TryGetValue(pair.Key, out var member)) continue;
            var candidates = InheritedDocumentationMembers(member).ToArray();
            var target = implicitTarget ? candidates.Select(DocumentationId).FirstOrDefault(members.ContainsKey)
                : members.ContainsKey(explicitTarget!) ? explicitTarget : null;
            if (target is null && candidates.Length > 0 && !string.IsNullOrWhiteSpace(options.AssemblyPath))
            {
                referenceInputs ??= WebApiDocumentationInputs.Discover(options.AssemblyPath);
                foreach (var candidate in candidates)
                {
                    var owner = candidate is Type candidateType ? candidateType.Assembly : candidate.DeclaringType!.Assembly;
                    var name = owner == typeof(object).Assembly ? "System.Runtime" : owner.GetName().Name;
                    foreach (var path in referenceInputs.Where(path => Path.GetFileNameWithoutExtension(path).Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!loadedReferences.Add(path)) continue;
                        var document = LoadXmlDocumentation(path);
                        foreach (var inherited in document.Root!.Element("members")!.Elements("member"))
                            if (inherited.Attribute("name")?.Value is { Length: > 0 } id) members.TryAdd(id, inherited);
                    }
                }
                target = implicitTarget ? candidates.Select(DocumentationId).FirstOrDefault(members.ContainsKey)
                    : members.ContainsKey(explicitTarget!) ? explicitTarget : null;
            }
            if (implicitTarget && target is not null) inheritDoc.SetAttributeValue("cref", target);
            else if (implicitTarget) warnings.Add($"Implicit inheritdoc could not be resolved for {pair.Key}; supply the inherited XML documentation or an explicit cref.");
        }
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
