namespace PowerForge;

/// <summary>Applies the target loader's documented file-name probing rules without accepting arbitrary suffixes.</summary>
internal static class PowerShellNativeLibraryName
{
    internal static bool FileNamesEqual(string runtimeIdentifier, string left, string right)
    {
        if (string.IsNullOrWhiteSpace(runtimeIdentifier) || string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        var comparison = runtimeIdentifier.StartsWith("win-", StringComparison.OrdinalIgnoreCase)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return GetFileName(left).Equals(GetFileName(right), comparison);
    }

    internal static bool CanResolve(string runtimeIdentifier, string requestedName, string deliveredName)
    {
        if (string.IsNullOrWhiteSpace(runtimeIdentifier) ||
            string.IsNullOrWhiteSpace(requestedName) ||
            string.IsNullOrWhiteSpace(deliveredName)) return false;

        var requested = GetFileName(requestedName);
        var delivered = GetFileName(deliveredName);
        if (runtimeIdentifier.StartsWith("win-", StringComparison.OrdinalIgnoreCase))
        {
            if (requested.Equals(delivered, StringComparison.OrdinalIgnoreCase)) return true;
            return !requested.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                   (requested + ".dll").Equals(delivered, StringComparison.OrdinalIgnoreCase);
        }

        if (runtimeIdentifier.StartsWith("linux-", StringComparison.OrdinalIgnoreCase))
            return GetUnixCandidates(requested, ".so").Contains(delivered, StringComparer.Ordinal);
        if (runtimeIdentifier.StartsWith("osx-", StringComparison.OrdinalIgnoreCase))
            return GetUnixCandidates(requested, ".dylib").Contains(delivered, StringComparer.Ordinal);
        return false;
    }

    private static IEnumerable<string> GetUnixCandidates(string requested, string extension)
    {
        yield return requested;
        if (requested.Contains(extension, StringComparison.Ordinal)) yield break;
        yield return requested + extension;
        if (!requested.StartsWith("lib", StringComparison.Ordinal))
        {
            yield return "lib" + requested;
            yield return "lib" + requested + extension;
        }
    }

    private static string GetFileName(string value)
    {
        var normalized = value.Replace('\\', '/');
        var separator = normalized.LastIndexOf('/');
        return separator < 0 ? normalized : normalized.Substring(separator + 1);
    }
}
