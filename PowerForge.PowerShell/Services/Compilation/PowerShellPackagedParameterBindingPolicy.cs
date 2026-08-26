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
        var authoredParameters = (ast.ParamBlock?.Parameters.AsEnumerable() ?? Enumerable.Empty<ParameterAst>()).ToArray();
        var readOnlyParameter = authoredParameters.FirstOrDefault(parameter =>
            PowerShellAssignmentTargetPolicy.IsReadOnlyAutomaticParameter(parameter.Name.VariablePath.UserPath, targetFramework));
        if (readOnlyParameter is not null)
        {
            throw new InvalidOperationException(
                $"Packaged parameter '${readOnlyParameter.Name.VariablePath.UserPath}' collides with a read-only automatic variable on target '{targetFramework}'.");
        }
        var mandatoryParameter = authoredParameters.FirstOrDefault(IsMandatory);
        if (mandatoryParameter is not null)
        {
            throw new InvalidOperationException(
                $"Packaged parameter '${mandatoryParameter.Name.VariablePath.UserPath}' is mandatory and may require interactive prompting when omitted; the embedded runspace has no console-backed PSHost. Use an optional parameter or a Strict executable contract.");
        }
        var commonParameters = PowerShellCommonParameterPolicy.GetAvailable(ast.ParamBlock, targetFramework);
        foreach (var parameter in authoredParameters)
        {
            var name = parameter.Name.VariablePath.UserPath;
            parameters.Add(name);
            if (parameter.StaticType == typeof(System.Management.Automation.SwitchParameter))
                switchParameters.Add(name);
            else if (parameter.StaticType == typeof(bool))
                booleanParameters.Add(name);
        }
        foreach (var commonParameter in commonParameters)
        {
            if (!parameters.Add(commonParameter.Name))
                throw AmbiguousBindingName(commonParameter.Name, commonParameter.Name, commonParameter.Name);
            if (commonParameter.IsSwitch)
                switchParameters.Add(commonParameter.Name);
        }
        foreach (var parameter in authoredParameters)
        {
            var name = parameter.Name.VariablePath.UserPath;
            foreach (var alias in parameter.Attributes.OfType<AttributeAst>().Where(static attribute => IsAttributeNamed(attribute, "Alias")))
            foreach (var value in alias.PositionalArguments.OfType<StringConstantExpressionAst>())
                AddAlias(parameterAliases, parameters, value.Value, name);
        }
        foreach (var commonParameter in commonParameters)
            AddAlias(parameterAliases, parameters, commonParameter.Alias, commonParameter.Name);
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

    private static void AddAlias(
        IDictionary<string, string> aliases,
        ISet<string> parameterNames,
        string alias,
        string owner)
    {
        if (parameterNames.Contains(alias))
            throw AmbiguousBindingName(alias, owner, alias);
        if (aliases.TryGetValue(alias, out var existingOwner))
            throw AmbiguousBindingName(alias, owner, existingOwner);
        aliases.Add(alias, owner);
    }

    private static InvalidOperationException AmbiguousBindingName(string name, string owner, string existingOwner)
        => new(
            $"Packaged parameter binding is ambiguous because binding name '{name}' for parameter '{owner}' conflicts with parameter '{existingOwner}' or one of its aliases.");

    private static bool IsAttributeNamed(AttributeAst attribute, string name)
    {
        var fullName = attribute.TypeName.FullName;
        return fullName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
               fullName.Equals(name + "Attribute", StringComparison.OrdinalIgnoreCase) ||
               fullName.EndsWith("." + name + "Attribute", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMandatory(ParameterAst parameter)
        => parameter.Attributes.OfType<AttributeAst>()
            .Where(static attribute => IsAttributeNamed(attribute, "Parameter"))
            .SelectMany(static attribute => attribute.NamedArguments)
            .Where(static argument => argument.ArgumentName.Equals("Mandatory", StringComparison.OrdinalIgnoreCase))
            .Any(static argument =>
            {
                try { return argument.Argument.SafeGetValue() is true; }
                catch (InvalidOperationException) { return false; }
            });
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
