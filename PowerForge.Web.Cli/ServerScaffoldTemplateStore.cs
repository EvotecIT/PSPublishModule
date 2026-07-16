using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge.Web.Cli;

internal static partial class ServerScaffoldTemplateStore
{
    private const string ResourcePrefix = "PowerForge.Web.Cli.Templates.ServerScaffold.";

    internal static string Render(string templateName, params (string Token, string Value)[] replacements)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateName);

        var resourceName = ResourcePrefix + templateName;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded server scaffold template not found: {resourceName}");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var rendered = reader.ReadToEnd();

        foreach (var (token, value) in replacements)
            rendered = rendered.Replace(token, value, StringComparison.Ordinal);

        var unresolved = ScaffoldTokenRegex().Matches(rendered).Select(static match => match.Value).Distinct(StringComparer.Ordinal).ToArray();
        if (unresolved.Length > 0)
            throw new InvalidOperationException($"Server scaffold template '{templateName}' has unresolved tokens: {string.Join(", ", unresolved)}");

        return rendered;
    }

    [GeneratedRegex("__[A-Z0-9_]+__", RegexOptions.CultureInvariant)]
    private static partial Regex ScaffoldTokenRegex();
}
