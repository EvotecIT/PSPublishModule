using System.Reflection;
using System.Xml.Linq;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    /// <summary>Supplies common framework contracts when the matching reference XML is unavailable.</summary>
    private static XElement? CreateFrameworkInheritDoc(MemberInfo member)
    {
        if (member is not MethodInfo method) return null;
        var declaring = method.DeclaringType!;
        var objectContract = declaring == typeof(object) || declaring == typeof(ValueType);
        var equatableContract = declaring.IsGenericType && declaring.GetGenericTypeDefinition() == typeof(IEquatable<>);
        // Compare actual framework types, not namespace prefixes that a library can also declare.
        if (!objectContract && !equatableContract) return null;
        var parameters = method.GetParameters();
        var documentation = new XElement("member", new XAttribute("name", DocumentationId(method)));
        if (method.Name == nameof(object.Equals) && parameters.Length == 1 && method.ReturnType == typeof(bool))
        {
            documentation.Add(
                new XElement("summary", "Determines whether the specified object is equal to the current object."),
                new XElement("param", new XAttribute("name", parameters[0].Name ?? (equatableContract ? "other" : "obj")),
                    "The object to compare with the current object."),
                new XElement("returns", "true if the specified object is equal to the current object; otherwise, false."));
        }
        else if (objectContract && parameters.Length == 0 && method.Name == nameof(object.GetHashCode) && method.ReturnType == typeof(int))
        {
            documentation.Add(
                new XElement("summary", "Returns a hash code for the current object."),
                new XElement("returns", "A hash code for the current object."));
        }
        else if (objectContract && parameters.Length == 0 && method.Name == nameof(object.ToString) && method.ReturnType == typeof(string))
        {
            documentation.Add(
                new XElement("summary", "Returns a string that represents the current object."),
                new XElement("returns", "A string that represents the current object."));
        }
        else return null;
        return documentation;
    }
}
