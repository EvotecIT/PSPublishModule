using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool TryExpandStableControlledImportAliases(
        string expression,
        IEnumerable<XDocument> sourceDocuments,
        IReadOnlyDictionary<string, string> evaluatedProperties,
        IReadOnlyDictionary<string, string>? immutableGlobalProperties,
        ISet<string> unstableProperties,
        out string expanded)
    {
        expanded = expression;
        XDocument[] documents = sourceDocuments.ToArray();
        bool DependsOnControlledEnvironment(string value)
            => documents.Any(document =>
                ConditionDependsOnControlledEnvironmentRewrite(value, document));
        for (int depth = 0; depth < 32; depth++)
        {
            MatchCollection references = Regex.Matches(expanded,
                @"\$\(([A-Za-z_][A-Za-z0-9_.-]*)\)", RegexOptions.CultureInvariant);
            if (references.Count == 0)
                return !ContainsUnresolvedBuildExpression(expanded) &&
                       expanded.IndexOfAny(new[] { '*', '?' }) < 0;

            foreach (string name in references.Cast<Match>()
                         .Select(match => match.Groups[1].Value)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (unstableProperties.Contains(name))
                    return false;

                XElement[] definitions = documents.SelectMany(document => document.Descendants())
                    .Where(element => element.Parent?.Name.LocalName.Equals(
                        "PropertyGroup", StringComparison.OrdinalIgnoreCase) == true &&
                        element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (definitions.Length == 0 &&
                    immutableGlobalProperties?.ContainsKey(name) != true)
                    return false;
                if (definitions.Any(definition =>
                        definition.Value.IndexOf("$([", StringComparison.Ordinal) >= 0 ||
                        Regex.IsMatch(definition.Value,
                            @"\$\([A-Za-z_][A-Za-z0-9_-]*\.",
                            RegexOptions.CultureInvariant) ||
                        ConditionDependsOnControlledContextValue(
                            definition.Value, documents, unstableProperties) ||
                        DependsOnControlledEnvironment(definition.Value) ||
                        definition.AncestorsAndSelf().SelectMany(element => element.Attributes())
                            .Where(attribute => attribute.Name.LocalName.Equals(
                                "Condition", StringComparison.OrdinalIgnoreCase))
                            .Any(attribute => ConditionDependsOnControlledContextValue(
                                    attribute.Value, documents, unstableProperties) ||
                                DependsOnControlledEnvironment(attribute.Value))))
                    return false;

                if (!evaluatedProperties.TryGetValue(name, out string? value))
                {
                    // A later checkout proof may omit this property. Every source
                    // declaration must then agree on one literal path value.
                    string[] literalValues = definitions.Select(definition =>
                            DecodeMsBuildEscapes(definition.Value))
                        .Distinct(StringComparer.Ordinal).ToArray();
                    if (literalValues.Length != 1 ||
                        ContainsUnresolvedBuildExpression(literalValues[0]))
                        return false;
                    value = literalValues[0];
                }
                expanded = ReplaceOrdinalIgnoreCase(expanded, "$(" + name + ")", value);
            }
        }
        return false;
    }
}
