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
        var commands = sources.SelectMany(static root => root
            .FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true).Cast<CommandAst>()
            .Select(command => (Root: root, Command: command))).ToArray();
        if (!commands.Any(static entry => IsPlainReadHost(entry.Command)))
            return;

        var aliases = ReadLiteralAliasTargets(commands.Select(static entry => entry.Command)
            .Where(PowerShellAliasDefinitionPolicy.IsAliasDefinitionCommand), out var unknown);
        unknown ??= commands.FirstOrDefault(entry => MayReplaceParameterDefaults(entry.Root, entry.Command, aliases)).Command;
        if (unknown is not null)
            throw UnverifiableReadHostDefaults(unknown);

        foreach (var root in sources)
        {
            var reference = root.Find(static node => node is VariableExpressionAst variable &&
                PowerShellAssignmentTargetPolicy.GetUnscopedVariableName(variable.VariablePath.UserPath)
                    .Equals(ParameterDefaults, StringComparison.OrdinalIgnoreCase) &&
                !UsesSafeReadHostDefaults(variable), searchNestedScriptBlocks: true)
                ?? FindIndirectVariableLookup(root, ParameterDefaults, topLevelOnly: false)
                ?? root.Find(static node => node is InvokeMemberExpressionAst method &&
                    MaySetParameterDefaults(method), searchNestedScriptBlocks: true);
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

    private static bool MaySetParameterDefaults(InvokeMemberExpressionAst method)
        => method.Member is StringConstantExpressionAst member &&
           member.Value.Equals("Set", StringComparison.OrdinalIgnoreCase) &&
           ReferencesPsVariableIntrinsics(method.Expression) &&
           (method.Arguments.Count != 2 || method.Arguments[0] is not StringConstantExpressionAst name ||
            PowerShellAssignmentTargetPolicy.GetUnscopedVariableName(name.Value)
                .Equals(ParameterDefaults, StringComparison.OrdinalIgnoreCase));

    private static bool MayReplaceParameterDefaults(ScriptBlockAst root, CommandAst command,
        IReadOnlyDictionary<string, HashSet<string>> aliases)
    {
        if (command.GetCommandName() is not { } name) return false;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ResolvesToCommandMatching(name, aliases, IsVariableSettingCommand, visited) &&
            GetVariableMayRetrieve(command, ParameterDefaults, allowScopePrefix: true))
            return true;

        visited.Clear();
        if (!ResolvesToCommandMatching(name, aliases, IsProviderSettingCommand, visited))
            return false;
        visited.Clear();
        var childName = ResolvesToCommandMatching(name, aliases, IsProviderCreationCommand, visited)
            ? ReadProviderChildName(command) : null;
        ExpressionAst? positional = null;
        for (var index = 1; index < command.CommandElements.Count; index++)
        {
            if (command.CommandElements[index] is not CommandParameterAst parameter)
            {
                positional ??= command.CommandElements[index] as ExpressionAst;
                continue;
            }
            if (IsProviderPathParameter(parameter.ParameterName))
            {
                var operand = parameter.Argument ?? command.CommandElements.ElementAtOrDefault(index + 1) as ExpressionAst;
                return operand is null || PowerShellPackagedPathOperandPolicy.MayRetrieveVariable(operand, ParameterDefaults, root, childName);
            }
            if (parameter.Argument is null && !IsProviderSettingSwitch(parameter.ParameterName) &&
                command.CommandElements.ElementAtOrDefault(index + 1) is ExpressionAst)
                index++;
        }
        return positional is not null &&
            PowerShellPackagedPathOperandPolicy.MayRetrieveVariable(positional, ParameterDefaults, root, childName);
    }

    private static string? ReadProviderChildName(CommandAst command)
    {
        for (var index = 1; index < command.CommandElements.Count; index++)
        {
            if (command.CommandElements[index] is not CommandParameterAst parameter ||
                !"Name".StartsWith(parameter.ParameterName, StringComparison.OrdinalIgnoreCase))
                continue;
            var value = parameter.Argument ?? command.CommandElements.ElementAtOrDefault(index + 1) as ExpressionAst;
            return (value as StringConstantExpressionAst)?.Value;
        }
        return null;
    }

    private static bool IsProviderPathParameter(string name)
        => "Path".StartsWith(name, StringComparison.OrdinalIgnoreCase) ||
           "LiteralPath".StartsWith(name, StringComparison.OrdinalIgnoreCase) ||
           name.Equals("PSPath", StringComparison.OrdinalIgnoreCase) || name.Equals("LP", StringComparison.OrdinalIgnoreCase);

    private static bool IsProviderSettingSwitch(string name)
        => new[] { "Force", "PassThru", "WhatIf", "Confirm", "Verbose", "Debug", "UseTransaction" }
            .Any(candidate => candidate.StartsWith(name, StringComparison.OrdinalIgnoreCase)) ||
           name.Equals("vb", StringComparison.OrdinalIgnoreCase) || name.Equals("db", StringComparison.OrdinalIgnoreCase);

    private static bool IsProviderSettingCommand(string name)
        => name.Split('\\').Last().ToUpperInvariant() is "SET-ITEM" or "NEW-ITEM" or "SI" or "NI";

    private static bool IsProviderCreationCommand(string name)
        => name.Split('\\').Last().ToUpperInvariant() is "NEW-ITEM" or "NI";

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
