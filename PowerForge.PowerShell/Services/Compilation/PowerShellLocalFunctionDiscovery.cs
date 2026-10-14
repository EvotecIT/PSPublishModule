using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Discovers top-level function names that can be resolved within one typed source closure.</summary>
internal static class PowerShellLocalFunctionDiscovery
{
    internal static HashSet<string> DiscoverNames(IEnumerable<string> files)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conditionalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var ast = Parser.ParseFile(file, out _, out ParseError[] errors);
            if (errors.Length > 0)
                continue;
            foreach (var declaration in PowerShellSourceParser.GetRootFunctionDeclarations(ast))
            {
                if (declaration.IsConditional) conditionalNames.Add(declaration.Syntax.Name);
                else names.Add(declaration.Syntax.Name);
            }
        }
        names.ExceptWith(conditionalNames);
        return names;
    }
}
