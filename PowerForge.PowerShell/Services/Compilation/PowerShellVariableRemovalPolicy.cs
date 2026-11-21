using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellVariableRemovalPolicy
{
    internal static Func<string, bool>? ReadAffectedNames(CommandAst command, out bool accessesEnclosingScope)
    {
        accessesEnclosingScope = false;
        var name = command.GetCommandName()?.Split('\\').Last();
        if (name is null)
            return null;
        bool removesVariables = name.Equals("Remove-Variable", StringComparison.OrdinalIgnoreCase) ||
                                name.Equals("rv", StringComparison.OrdinalIgnoreCase);
        bool variableCommand = removesVariables || IsVariableCommand(name);
        if (!variableCommand && !RemovesProviderItems(name))
            return null;

        StaticBindingResult binding;
        try
        {
            binding = StaticParameterBinder.BindCommand(command);
        }
        catch (RuntimeException)
        {
            accessesEnclosingScope = variableCommand;
            return removesVariables || !variableCommand ? _ => true : null;
        }

        if (variableCommand && binding.BoundParameters.TryGetValue("Scope", out var scope) &&
            !IsLocalScope(scope.ConstantValue))
        {
            accessesEnclosingScope = true;
            return null;
        }
        if (variableCommand && command.CommandElements.OfType<VariableExpressionAst>().Any(variable => variable.Splatted))
            accessesEnclosingScope = true;
        if (variableCommand && !removesVariables)
            return null;

        var key = removesVariables ? "Name" : binding.BoundParameters.ContainsKey("LiteralPath") ? "LiteralPath" : "Path";
        if (!binding.BoundParameters.TryGetValue(key, out var argument))
            return _ => true;
        IEnumerable<Ast> values = argument.Value is ArrayLiteralAst array ? array.Elements : new[] { argument.Value };
        var targets = new List<string>();
        foreach (var value in values)
        {
            if (value is not StringConstantExpressionAst literal)
                return _ => true;
            if (removesVariables)
                targets.Add(literal.Value);
            else if (ReadVariableProviderName(literal.Value) is { } variable)
                targets.Add(variable);
        }

        if (targets.Count == 0)
            return null;
        if (key == "LiteralPath")
            return variable => targets.Contains(variable, StringComparer.OrdinalIgnoreCase);
        var patterns = targets.Select(target => new WildcardPattern(target, WildcardOptions.IgnoreCase)).ToArray();
        return variable => patterns.Any(pattern => pattern.IsMatch(variable));
    }

    private static bool IsLocalScope(object? scope)
        => scope is int and 0 || scope is string name &&
           (name.Equals("Local", StringComparison.OrdinalIgnoreCase) || name.Equals("0", StringComparison.Ordinal));

    private static bool IsVariableCommand(string command)
        => new[] { "Get-Variable", "Set-Variable", "New-Variable", "Clear-Variable", "gv", "sv", "nv", "clv" }
            .Contains(command, StringComparer.OrdinalIgnoreCase);

    private static bool RemovesProviderItems(string command)
        => command.Equals("Remove-Item", StringComparison.OrdinalIgnoreCase) ||
           new[] { "ri", "rm", "del", "erase", "rmdir", "rd" }.Contains(command, StringComparer.OrdinalIgnoreCase);

    private static string? ReadVariableProviderName(string path)
    {
        const string drive = "Variable:";
        const string provider = "Microsoft.PowerShell.Core\\Variable::";
        int nameOffset = path.StartsWith(provider, StringComparison.OrdinalIgnoreCase) ? provider.Length :
            path.StartsWith(drive, StringComparison.OrdinalIgnoreCase) ? drive.Length : 0;
        if (nameOffset == 0)
            return null;
        return path.Substring(nameOffset).TrimStart(':', '\\', '/');
    }
}
