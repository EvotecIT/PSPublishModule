using System.Reflection;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    /// <summary>Keeps the closest declaration for a callable signature or named member.</summary>
    private static IEnumerable<TMember> SelectVisibleAssemblyMembers<TMember>(Type type,
        IEnumerable<TMember> members, Func<TMember, string> signature) where TMember : MemberInfo
    {
        var distances = new Dictionary<Type, int>();
        for (var ancestor = type; ancestor is not null; ancestor = ancestor.BaseType)
            distances[ancestor] = distances.Count;

        return members
            .OrderBy(member => member.DeclaringType is not null && distances.TryGetValue(member.DeclaringType, out var distance)
                ? distance : int.MaxValue)
            .DistinctBy(signature, StringComparer.Ordinal);
    }

    /// <summary>Prevents XML for non-public hiders from being attached to inherited public members.</summary>
    private static void RestrictToDeclaredXmlSurface(ApiTypeModel model, Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var methods = type.GetMethods(flags).Select(BuildDocumentationMethodSignature).ToHashSet(StringComparer.Ordinal);
        var properties = type.GetProperties(flags).Select(BuildDocumentationPropertySignature).ToHashSet(StringComparer.Ordinal);
        var fields = type.GetFields(flags).Select(static member => member.Name).ToHashSet(StringComparer.Ordinal);
        var events = type.GetEvents(flags).Select(static member => member.Name).ToHashSet(StringComparer.Ordinal);

        model.Methods.RemoveAll(member => !methods.Contains(member.DocumentationSignature ?? string.Empty));
        model.Properties.RemoveAll(member => !properties.Contains(member.DocumentationSignature ?? string.Empty));
        model.Fields.RemoveAll(member => !fields.Contains(member.Name));
        model.Events.RemoveAll(member => !events.Contains(member.Name));
    }
}
