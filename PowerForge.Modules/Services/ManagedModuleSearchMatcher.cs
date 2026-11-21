using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

internal static class ManagedModuleSearchMatcher
{
    public static bool HasWildcard(string value)
        => value.IndexOf('*') >= 0 || value.IndexOf('?') >= 0;

    public static bool IsMatch(string pattern, string value)
    {
        if (HasWildcard(pattern))
            return ToRegex(pattern).IsMatch(value);

        return value.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static string ToSearchText(string pattern)
        => HasWildcard(pattern)
            ? pattern.Replace("*", string.Empty).Replace("?", string.Empty).Trim()
            : pattern.Trim();

    private static Regex ToRegex(string pattern)
    {
        var builder = new StringBuilder("^");
        foreach (var character in pattern)
        {
            builder.Append(character switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(character.ToString())
            });
        }

        builder.Append('$');
        return new Regex(builder.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
