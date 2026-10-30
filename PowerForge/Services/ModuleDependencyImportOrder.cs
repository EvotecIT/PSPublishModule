using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

/// <summary>Orders selected modules without letting a consumer autoload another dependency version first.</summary>
internal static class ModuleDependencyImportOrder
{
    internal static string[] Resolve(string[] preferred, IReadOnlyDictionary<string, string[]> dependencies)
    {
        var remaining = new HashSet<string>(preferred, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(preferred.Length);
        while (remaining.Count > 0)
        {
            var next = preferred.FirstOrDefault(name => remaining.Contains(name) &&
                (!dependencies.TryGetValue(name, out var required) || !required.Any(remaining.Contains)));
            if (next is null)
                throw new InvalidOperationException($"RequiredModules contain a dependency cycle: {string.Join(", ", remaining.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase))}.");
            result.Add(next);
            remaining.Remove(next);
        }
        return result.ToArray();
    }
}
