using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellVariableRemovalPolicy
{
    internal static Func<string, bool>? ReadAffectedNames(CommandAst command)
    {
        var name = command.GetCommandName()?.Split('\\').Last();
        if (name is null)
            return null;
        bool removesVariables = name.Equals("Remove-Variable", StringComparison.OrdinalIgnoreCase) ||
                                name.Equals("rv", StringComparison.OrdinalIgnoreCase);
        if (!removesVariables && !RemovesProviderItems(name))
            return null;

        StaticBindingResult binding;
        try
        {
            binding = StaticParameterBinder.BindCommand(command);
        }
        catch (RuntimeException)
        {
            return _ => true;
        }

        if (removesVariables && binding.BoundParameters.TryGetValue("Scope", out var scope) &&
            scope.ConstantValue is string scopeName &&
            (scopeName.Equals("Global", StringComparison.OrdinalIgnoreCase) ||
             scopeName.Equals("Script", StringComparison.OrdinalIgnoreCase)))
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
