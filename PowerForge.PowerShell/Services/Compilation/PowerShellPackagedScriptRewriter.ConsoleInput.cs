using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

internal static partial class PowerShellPackagedScriptRewriter
{
    private const string ParameterDefaults = "PSDefaultParameterValues";

    private static void ValidateReadHostDefaults(ScriptBlockAst entry, string entryPath, IEnumerable<string> embeddedSources)
    {
        var sources = embeddedSources
            .Where(path => !PowerShellCompilationPathSafety.PathEquals(path, entryPath))
            .Select(static path => Parser.ParseFile(path, out _, out _))
            .Prepend(entry)
            .ToArray();
        var commands = sources.SelectMany(static root =>
            root.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true).Cast<CommandAst>()).ToArray();
        if (!commands.Any(IsPlainReadHost))
            return;

        var aliases = ReadLiteralAliasTargets(commands.Where(PowerShellAliasDefinitionPolicy.IsAliasDefinitionCommand), out var unknown);
        unknown ??= commands.FirstOrDefault(command => MayReplaceParameterDefaults(command, aliases));
        if (unknown is not null)
            throw UnverifiableReadHostDefaults(unknown);

        foreach (var root in sources)
        {
            var reference = root.Find(static node => node is VariableExpressionAst variable &&
                PowerShellAssignmentTargetPolicy.GetUnscopedVariableName(variable.VariablePath.UserPath)
                    .Equals(ParameterDefaults, StringComparison.OrdinalIgnoreCase) &&
                !UsesSafeReadHostDefaults(variable), searchNestedScriptBlocks: true)
                ?? FindIndirectVariableLookup(root, ParameterDefaults, topLevelOnly: false);
            if (reference is not null)
                throw UnverifiableReadHostDefaults(reference);
        }
    }

    private static InvalidOperationException UnverifiableReadHostDefaults(Ast node)
        => new($"Packaged executable generation cannot verify plain Read-Host input with parameter defaults at line {node.Extent.StartLineNumber}.");

    private static bool UsesSafeReadHostDefaults(VariableExpressionAst variable)
    {
        if (variable.Parent is IndexExpressionAst { Index: StringConstantExpressionAst key } index &&
            ReferenceEquals(index.Target, variable))
            return !CanSelectProtectedInput(key.Value);

        if (variable.Parent is not AssignmentStatementAst
            {
                Operator: TokenKind.Equals,
                Right: CommandExpressionAst { Redirections.Count: 0, Expression: HashtableAst table }
            } assignment || !ReferenceEquals(assignment.Left, variable))
            return false;

        return table.KeyValuePairs.All(static pair =>
            pair.Item1 is StringConstantExpressionAst name && !CanSelectProtectedInput(name.Value));
    }

    private static bool CanSelectProtectedInput(string key)
    {
        var separator = key.LastIndexOf(':');
        if (separator < 0)
            return false;

        var command = new WildcardPattern(key.Substring(0, separator).Trim(), WildcardOptions.IgnoreCase);
        if (!command.IsMatch("Read-Host") && !command.IsMatch("Microsoft.PowerShell.Utility\\Read-Host"))
            return false;
        var name = key.Substring(separator + 1).Trim();
        var parameter = new WildcardPattern(name, WildcardOptions.IgnoreCase);
        return parameter.IsMatch("AsSecureString") || parameter.IsMatch("MaskInput") ||
               "AsSecureString".StartsWith(name, StringComparison.OrdinalIgnoreCase) ||
               "MaskInput".StartsWith(name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MayReplaceParameterDefaults(CommandAst command, IReadOnlyDictionary<string, HashSet<string>> aliases)
        => command.GetCommandName() is { } name &&
           ResolvesToCommandMatching(name, aliases, IsVariableSettingCommand, new HashSet<string>(StringComparer.OrdinalIgnoreCase)) &&
           GetVariableMayRetrieve(command, ParameterDefaults, allowScopePrefix: true);

    private static bool IsVariableSettingCommand(string name)
    {
        var unqualified = name.Split('\\').Last();
        return unqualified.Equals("Set-Variable", StringComparison.OrdinalIgnoreCase) ||
               unqualified.Equals("New-Variable", StringComparison.OrdinalIgnoreCase) ||
               unqualified.Equals("set", StringComparison.OrdinalIgnoreCase) ||
               unqualified.Equals("sv", StringComparison.OrdinalIgnoreCase) ||
               unqualified.Equals("nv", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlainReadHost(CommandAst command)
    {
        var name = command.GetCommandName();
        if (!string.Equals(name, "Read-Host", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "Microsoft.PowerShell.Utility\\Read-Host", StringComparison.OrdinalIgnoreCase))
            return false;
        return command.CommandElements.Skip(1).All(static element =>
            element is not VariableExpressionAst { Splatted: true } &&
            (element is not CommandParameterAst parameter ||
             !("AsSecureString".StartsWith(parameter.ParameterName, StringComparison.OrdinalIgnoreCase) ||
               "MaskInput".StartsWith(parameter.ParameterName, StringComparison.OrdinalIgnoreCase))));
    }
}
