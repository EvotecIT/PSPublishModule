using System.Reflection;
using System.Xml.Linq;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    /// <summary>Supplies narrow framework contracts when targeting-pack XML is unavailable.</summary>
    private static XElement? TryCreateFrameworkDocumentation(MemberInfo member, string id)
    {
        var declaring = member.DeclaringType;
        if (declaring?.Assembly != typeof(object).Assembly)
            return null;

        var summary = id switch
        {
            "M:System.Object.Equals(System.Object)" or "M:System.ValueType.Equals(System.Object)" =>
                "Determines whether the specified object is equal to the current object.",
            "M:System.Object.GetHashCode" or "M:System.ValueType.GetHashCode" =>
                "Returns a hash code for the current object.",
            "M:System.Object.ToString" or "M:System.ValueType.ToString" =>
                "Returns a string that represents the current object.",
            "M:System.IEquatable`1.Equals(`0)" =>
                "Determines whether the specified value is equal to the current instance.",
            _ => null
        };
        return summary is null ? null : new XElement("member", new XAttribute("name", id), new XElement("summary", summary));
    }
}
