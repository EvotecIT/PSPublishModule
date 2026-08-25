using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Produces the authored and automatic parameter catalog used by packaged executable argument binding.
/// </summary>
internal static class PowerShellPackagedParameterBindingPolicy
{
    internal static PowerShellPackagedParameterInitializers Generate(string sourcePath, string targetFramework)
    {
        var ast = Parser.ParseFile(sourcePath, out _, out var errors);
        if (errors.Length > 0)
            throw new InvalidOperationException("Packaged script parameters could not be parsed for native argument binding.");

        var parameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var switchParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var booleanParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parameterAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in ast.ParamBlock?.Parameters.AsEnumerable() ?? Enumerable.Empty<ParameterAst>())
        {
            var name = parameter.Name.VariablePath.UserPath;
            parameters.Add(name);
            foreach (var alias in parameter.Attributes.OfType<AttributeAst>().Where(static attribute => IsAttributeNamed(attribute, "Alias")))
            foreach (var value in alias.PositionalArguments.OfType<StringConstantExpressionAst>())
                parameterAliases[value.Value] = name;
            if (parameter.StaticType == typeof(System.Management.Automation.SwitchParameter))
                switchParameters.Add(name);
            else if (parameter.StaticType == typeof(bool))
                booleanParameters.Add(name);
        }
        foreach (var commonParameter in PowerShellCommonParameterPolicy.GetAvailable(ast.ParamBlock, targetFramework))
        {
            parameters.Add(commonParameter.Name);
            parameterAliases[commonParameter.Alias] = commonParameter.Name;
            if (commonParameter.IsSwitch)
                switchParameters.Add(commonParameter.Name);
        }
        return new PowerShellPackagedParameterInitializers(
            GenerateInitializer(parameters),
            GenerateInitializer(switchParameters),
            GenerateInitializer(booleanParameters),
            GenerateAliasInitializer(parameterAliases));
    }

    private static string GenerateInitializer(IEnumerable<string> values)
        => string.Join(", ", values
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .Select(PowerShellCSharpLiteral.QuoteString));

    private static string GenerateAliasInitializer(IEnumerable<KeyValuePair<string, string>> aliases)
        => string.Join(", ", aliases
            .OrderBy(static alias => alias.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static alias => $"[{PowerShellCSharpLiteral.QuoteString(alias.Key)}] = {PowerShellCSharpLiteral.QuoteString(alias.Value)}"));

    private static bool IsAttributeNamed(AttributeAst attribute, string name)
    {
        var fullName = attribute.TypeName.FullName;
        return fullName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
               fullName.Equals(name + "Attribute", StringComparison.OrdinalIgnoreCase) ||
               fullName.EndsWith("." + name + "Attribute", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class PowerShellPackagedParameterInitializers
{
    internal PowerShellPackagedParameterInitializers(string parameters, string switchParameters, string booleanParameters, string parameterAliases)
    {
        Parameters = parameters;
        SwitchParameters = switchParameters;
        BooleanParameters = booleanParameters;
        ParameterAliases = parameterAliases;
    }

    internal string Parameters { get; }

    internal string SwitchParameters { get; }

    internal string BooleanParameters { get; }

    internal string ParameterAliases { get; }
}
