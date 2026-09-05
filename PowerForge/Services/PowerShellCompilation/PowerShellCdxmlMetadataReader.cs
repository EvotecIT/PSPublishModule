using System.Globalization;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace PowerForge;

/// <summary>Parses bounded CDXML metadata without importing a module or contacting a management target.</summary>
public sealed class PowerShellCdxmlMetadataReader
{
    private const long MaximumCdxmlBytes = 8L * 1024L * 1024L;

    /// <summary>Reads one CDXML document into deterministic portable metadata.</summary>
    public PowerShellCdxmlMetadata Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A CDXML path is required.", nameof(path));
        var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException("CDXML metadata was not found.", fullPath);
        if (info.Length > MaximumCdxmlBytes)
            throw new InvalidOperationException($"CDXML metadata exceeds the {MaximumCdxmlBytes.ToString(CultureInfo.InvariantCulture)} byte safety limit.");
        byte[] bytes = File.ReadAllBytes(fullPath);
        XDocument document;
        using (var stream = new MemoryStream(bytes, writable: false))
        using (var reader = XmlReader.Create(stream, new XmlReaderSettings
               {
                   DtdProcessing = DtdProcessing.Prohibit,
                   XmlResolver = null,
                   MaxCharactersInDocument = MaximumCdxmlBytes
               }))
        {
            document = XDocument.Load(reader, LoadOptions.None);
        }

        var root = document.Root ?? throw new InvalidOperationException("CDXML metadata has no document element.");
        if (!root.Name.LocalName.Equals("PowerShellMetadata", StringComparison.Ordinal))
            throw new InvalidOperationException($"CDXML root '{root.Name.LocalName}' is not PowerShellMetadata.");
        var classElement = root.Descendants().FirstOrDefault(static element => element.Name.LocalName == "Class")
            ?? throw new InvalidOperationException("CDXML metadata does not declare a management Class.");
        var className = Attribute(classElement, "ClassName");
        if (className.Length == 0) throw new InvalidOperationException("CDXML management Class requires ClassName.");
        var defaultNoun = classElement.Elements().FirstOrDefault(static element => element.Name.LocalName == "DefaultNoun")?.Value.Trim() ?? string.Empty;
        var version = classElement.Elements().FirstOrDefault(static element => element.Name.LocalName == "Version")?.Value.Trim() ?? string.Empty;
        var commands = ParseCommands(classElement, defaultNoun);
        return new PowerShellCdxmlMetadata
        {
            SchemaUri = root.Name.NamespaceName,
            ClassName = className,
            ClassVersion = version,
            DefaultNoun = defaultNoun,
            Commands = commands,
            SourceSha256 = Hash(bytes)
        };
    }

    private static PowerShellCdxmlCommand[] ParseCommands(XElement classElement, string defaultNoun)
    {
        var commands = new List<PowerShellCdxmlCommand>();
        foreach (var element in classElement.Descendants().Where(static element =>
                     element.Name.LocalName is "GetCmdlet" or "Cmdlet"))
        {
            var metadata = element.DescendantsAndSelf().FirstOrDefault(static item => item.Name.LocalName == "CmdletMetadata");
            var verb = metadata is null ? string.Empty : Attribute(metadata, "Verb");
            var noun = metadata is null ? string.Empty : Attribute(metadata, "Noun");
            if (element.Name.LocalName == "GetCmdlet" && verb.Length == 0) verb = "Get";
            if (noun.Length == 0) noun = defaultNoun;
            if (verb.Length == 0 || noun.Length == 0) continue;
            var method = element.DescendantsAndSelf().FirstOrDefault(static item => item.Name.LocalName == "Method");
            var parameters = element.Descendants()
                .Select(static parameter =>
                {
                    var name = Attribute(parameter, "ParameterName");
                    return name.Length == 0 ? Attribute(parameter, "PSName") : name;
                })
                .Where(static name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            commands.Add(new PowerShellCdxmlCommand
            {
                CommandName = verb + "-" + noun,
                MethodName = method is null ? (element.Name.LocalName == "GetCmdlet" ? "Query" : string.Empty) : Attribute(method, "MethodName"),
                Parameters = parameters
            });
        }
        return commands
            .GroupBy(static command => command.CommandName, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static command => command.CommandName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string Attribute(XElement element, string name)
        => element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.Equals(name, StringComparison.Ordinal))?.Value.Trim() ?? string.Empty;

    private static string Hash(byte[] bytes)
    {
        using var algorithm = SHA256.Create();
        return string.Concat(algorithm.ComputeHash(bytes).Select(static value => value.ToString("x2", CultureInfo.InvariantCulture)));
    }
}
