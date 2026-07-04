using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

internal static class ModuleStateInventoryFilter
{
    internal static ModuleStateInventory Apply(ModuleStateInventory inventory, ModuleStateInventoryRequest request)
    {
        if (inventory is null)
            throw new ArgumentNullException(nameof(inventory));
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var modules = inventory.InstalledModules.AsEnumerable();
        if (request.Names.Length > 0)
        {
            var filters = request.Names
                .Select(static name => new Regex("^" + WildcardToRegex(name) + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                .ToArray();
            modules = modules.Where(module => filters.Any(filter => filter.IsMatch(module.Name)));
        }

        if (!string.IsNullOrWhiteSpace(request.Version))
            modules = modules.Where(module => VersionMatches(module.Version, request.Version!));

        if (!string.IsNullOrWhiteSpace(request.Scope))
            modules = modules.Where(module => string.Equals(module.Scope, request.Scope, StringComparison.OrdinalIgnoreCase));

        return new ModuleStateInventory(modules);
    }

    private static bool VersionMatches(string installedVersion, string requestedVersion)
    {
        var trimmed = requestedVersion.Trim();
        if (!LooksLikeRange(trimmed))
        {
            if (ModuleStateVersion.TryParse(installedVersion, out var installed) &&
                ModuleStateVersion.TryParse(trimmed, out var requested))
                return installed.CompareTo(requested) == 0;

            return string.Equals(installedVersion, trimmed, StringComparison.OrdinalIgnoreCase);
        }

        var range = ManagedModuleVersionRange.Parse(trimmed);
        return range.IsSatisfiedBy(installedVersion);
    }

    private static bool LooksLikeRange(string version)
        => version.StartsWith("=", StringComparison.Ordinal) ||
           version.StartsWith("<", StringComparison.Ordinal) ||
           version.StartsWith(">", StringComparison.Ordinal) ||
           version.StartsWith("[", StringComparison.Ordinal) ||
           version.StartsWith("(", StringComparison.Ordinal) ||
           version.EndsWith("]", StringComparison.Ordinal) ||
           version.EndsWith(")", StringComparison.Ordinal) ||
           version.Contains(",", StringComparison.Ordinal);

    private static string WildcardToRegex(string wildcard)
    {
        var builder = new StringBuilder();
        foreach (var character in wildcard)
        {
            if (character == '*')
                builder.Append(".*");
            else if (character == '?')
                builder.Append('.');
            else
                builder.Append(Regex.Escape(character.ToString()));
        }

        return builder.ToString();
    }
}
