namespace PowerForge;

/// <summary>Discovers top-level function names that can be resolved within one typed source closure.</summary>
internal static class PowerShellLocalFunctionDiscovery
{
    internal static HashSet<string> DiscoverNames(IEnumerable<ParsedSourceDocument> documents)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conditionalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in documents)
        {
            if (document.Errors.Length > 0)
                continue;
            foreach (var declaration in document.RootFunctionDeclarations)
            {
                if (declaration.IsConditional) conditionalNames.Add(declaration.Syntax.Name);
                else names.Add(declaration.Syntax.Name);
            }
        }
        names.ExceptWith(conditionalNames);
        return names;
    }
}
