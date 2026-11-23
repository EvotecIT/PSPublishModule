using System.Management.Automation.Language;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Recognizes nominal names resolved by PowerShell during or after module initialization.</summary>
internal static class PowerShellModuleTypeLoadPolicy
{
    // This selects likely declarations for native lookup. The C# compiler and the runspace
    // still own compilation, conditional availability, name resolution and errors.
    private static readonly Regex PublicTypeName = new(
        @"^[ \t]*public[ \t]+(?:(?:sealed|abstract|static)[ \t]+)?(?:class|struct|enum)[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)[ \t]*(?=[:{\r\n])",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    internal static HashSet<string> Discover(IEnumerable<ParsedSourceDocument> documents)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in documents)
        {
            var root = document.NativeScriptRootName == PowerShellNativeModuleInitializerPolicy.MethodName
                ? document.RootFunctionDeclarations.Single().Syntax.Body : document.SyntaxRoot;
            foreach (var pipeline in root.FindAll(static node => node is PipelineAst,
                         searchNestedScriptBlocks: false).OfType<PipelineAst>())
            {
                if (!PowerShellHostedGuardedAddTypePolicy.TryReadLiteralDefinition(pipeline, out var definition))
                    continue;
                foreach (Match match in PublicTypeName.Matches(definition))
                    names.Add(match.Groups["name"].Value);
            }
        }
        return names;
    }

    internal static HashSet<string> FindLexicalTypeAnchors(ParsedSourceDocument document, FunctionDefinitionAst function)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (document.TypeClosure.Declarations.Length == 0 || function.Body.ParamBlock is not { } parameters)
            return names;
        var types = parameters.Parameters.SelectMany(static parameter => parameter.Attributes.OfType<TypeConstraintAst>())
            .Select(static constraint => constraint.TypeName)
            .Concat(parameters.Attributes.OfType<AttributeAst>().Where(static attribute =>
                    attribute.TypeName.Name.Equals("OutputType", StringComparison.OrdinalIgnoreCase) ||
                    attribute.TypeName.Name.Equals("OutputTypeAttribute", StringComparison.OrdinalIgnoreCase))
                .SelectMany(static attribute => attribute.PositionalArguments.OfType<TypeExpressionAst>())
                .Select(static expression => expression.TypeName));
        foreach (var type in types)
            if (type is TypeName && string.IsNullOrEmpty(type.AssemblyName) &&
                document.TypeClosure.Declarations.Count(declaration => declaration.IsDocumentScope &&
                    declaration.Name.Equals(type.FullName, StringComparison.OrdinalIgnoreCase)) == 1)
                names.Add(type.FullName);
        return names;
    }
}
