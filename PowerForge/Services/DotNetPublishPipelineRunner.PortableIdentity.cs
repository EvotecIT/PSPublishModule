using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal static string[] ResolvePortableExecutableIdentities(
        string projectPath,
        string? configuredIdentity)
    {
        if (!string.IsNullOrWhiteSpace(configuredIdentity))
            return new[] { NormalizePortableExecutableIdentity(configuredIdentity!) };

        var identities = new List<string>();
        try
        {
            XDocument document = XDocument.Load(projectPath, LoadOptions.None);
            foreach (string propertyName in new[] { "Product", "AssemblyName" })
            {
                identities.AddRange(document
                    .Descendants()
                    .Where(element => string.Equals(
                        element.Name.LocalName,
                        propertyName,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(element => element.Value)
                    .Where(IsStaticPortableExecutableIdentity)
                    .Select(NormalizePortableExecutableIdentity));
            }
        }
        catch (IOException)
        {
            // The caller separately validates the project input. Fall back to its stable file identity.
        }
        catch (System.Xml.XmlException)
        {
            // The caller separately validates the project input. Fall back to its stable file identity.
        }

        identities.Add(NormalizePortableExecutableIdentity(Path.GetFileNameWithoutExtension(projectPath)));
        return identities
            .Where(identity => !string.IsNullOrWhiteSpace(identity))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static bool PortableExecutableIdentityMatches(
        string actualIdentity,
        IEnumerable<string>? expectedIdentities)
    {
        string actual = NormalizePortableExecutableIdentity(actualIdentity);
        return (expectedIdentities ?? Array.Empty<string>()).Any(expected => string.Equals(
            actual,
            NormalizePortableExecutableIdentity(expected),
            StringComparison.OrdinalIgnoreCase));
    }

    internal static string ResolvePortableExecutableIdentity(
        string? productName,
        string? internalName,
        string? originalFileName,
        string executablePath)
        => FirstText(ResolvePortableExecutableIdentityCandidates(
            productName,
            internalName,
            originalFileName,
            executablePath));

    internal static string[] ResolvePortableExecutableIdentityCandidates(
        string? productName,
        string? internalName,
        string? originalFileName,
        string executablePath)
        => new[]
        {
            productName,
            internalName,
            originalFileName,
            Path.GetFileNameWithoutExtension(executablePath)
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!.Trim())
        .ToArray();

    private static bool IsStaticPortableExecutableIdentity(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value!.IndexOf("$(", StringComparison.Ordinal) < 0 &&
        value.IndexOf("@(", StringComparison.Ordinal) < 0;

    private static string NormalizePortableExecutableIdentity(string value)
    {
        string normalized = value.Trim();
        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            normalized = Path.GetFileNameWithoutExtension(normalized);
        }
        return normalized;
    }
}
