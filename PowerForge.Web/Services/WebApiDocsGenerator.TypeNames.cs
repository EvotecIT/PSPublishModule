using System.Globalization;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private sealed class ApiTypeDisplayNameMap : Dictionary<string, string>
    {
        internal ApiTypeDisplayNameMap() : base(StringComparer.OrdinalIgnoreCase) { }
        internal HashSet<string> AmbiguousSlugs { get; } = new(StringComparer.OrdinalIgnoreCase);

        internal ApiTypeDisplayNameMap IndexCollisions()
        {
            foreach (var group in this.GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
                foreach (var pair in group) AmbiguousSlugs.Add(pair.Key);
            return this;
        }
    }

    // Keep documentation identities and routes separate from human-readable generic names.
    private sealed class ApiTypeLinkMap : Dictionary<string, string>
    {
        internal ApiTypeLinkMap() : base(StringComparer.OrdinalIgnoreCase) { }
        internal Dictionary<string, string> DefinitionNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, string> QualifiedNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> AmbiguousIdentities { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static string GetTypeDefinitionName(ApiTypeModel type, bool qualified = false)
    {
        if (IsPowerShellCommandType(type)) return qualified ? type.FullName : type.Name;
        var name = type.FullName;
        if (!qualified && !string.IsNullOrEmpty(type.Namespace) && name.StartsWith(type.Namespace + ".", StringComparison.Ordinal))
            name = name[(type.Namespace.Length + 1)..];
        var parameters = type.TypeParameters.Select(parameter => parameter.Name).ToArray();
        // XML for a nested type documents its own parameters; reflection supplies the
        // complete declaring-type chain. Align partial XML metadata to the inner type.
        var arity = Regex.Matches(name, @"`(\d+)").Sum(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        var offset = parameters.Length - arity;
        return Regex.Replace(name, @"`(\d+)", match =>
        {
            var count = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var names = Enumerable.Range(0, count).Select(index => offset + index >= 0 && offset + index < parameters.Length
                ? parameters[offset + index] : count == 1 ? "T" : "T" + (index + 1)).ToArray();
            offset += count;
            return "<" + string.Join(", ", names) + ">";
        });
    }

    private static string BuildTypeLinkAttributes(ApiTypeModel type, IReadOnlyDictionary<string, string> displayNames)
    {
        var qualified = GetTypeDefinitionName(type, qualified: true);
        var ambiguous = displayNames is ApiTypeDisplayNameMap names && names.AmbiguousSlugs.Contains(type.Slug);
        var title = type.FullName.Contains('`') || ambiguous
            ? $" title=\"{System.Web.HttpUtility.HtmlAttributeEncode(qualified)}\"" : BuildAliasTitleAttribute(type);
        return title + (ambiguous ? $" aria-label=\"{System.Web.HttpUtility.HtmlAttributeEncode(qualified)}\"" : string.Empty);
    }

    private static string RenderPowerShellTypeReference(string? name, string baseUrl, IReadOnlyDictionary<string, string> slugMap)
        => !string.IsNullOrEmpty(name) && name.IndexOfAny(new[] { '`', '{', '<' }) >= 0
            ? LinkifyType(name, baseUrl, slugMap) : System.Web.HttpUtility.HtmlEncode(name ?? string.Empty);
}
