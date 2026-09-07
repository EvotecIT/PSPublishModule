using System.Text;
using System.Xml.Linq;
using PowerForge.Compilation.Build;

namespace PowerForge;

/// <summary>Embeds command-name finalization in independently rebuildable generated projects.</summary>
internal static class PowerShellCommandMetadataBuildSupport
{
    internal const string PackageVersion = "10.0.10";
    internal static IReadOnlyList<string> PackageIds { get; } = Array.AsReadOnly(new[] { "System.Reflection.Metadata", "System.Collections.Immutable" });

    internal static bool RequiresBuildTool(PowerShellCompilationArtifactKind kind, PowerShellCompilationMode mode)
        => kind == PowerShellCompilationArtifactKind.BinaryModule ||
           kind == PowerShellCompilationArtifactKind.Executable && mode == PowerShellCompilationMode.Hybrid;

    internal static void Write(string workspace, string projectPath, PowerShellTypedCompilationResult typed, IEnumerable<string>? exportedFunctions)
    {
        var directory = Path.Combine(workspace, "Build");
        Directory.CreateDirectory(directory);
        var identities = PowerShellBinaryCmdletSourceGenerator.GetCommandIdentities(typed, exportedFunctions);
        var items = string.Join(Environment.NewLine, identities.Select(identity => new XElement("PowerForgeCommandIdentity",
            new XAttribute("Include", EscapeProjectLiteral(identity.TypeName)),
            new XElement("CommandName", EscapeProjectLiteral(identity.CommandName)),
            new XElement("StorageField", EscapeProjectLiteral(identity.StorageField))).ToString(SaveOptions.DisableFormatting)));
        File.WriteAllText(Path.Combine(directory, "CommandMetadata.targets"),
            Read("CommandMetadata.targets.template").Replace("{{COMMAND_IDENTITIES}}", items), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "CommandMetadataTask.cs"),
            Read("PowerShellCommandMetadataNames.cs") + Environment.NewLine + Read("PowerShellCommandMetadataNames.Strings.cs") +
            Environment.NewLine + Read("CommandMetadataTask.cs.template"), new UTF8Encoding(false));
        var project = XDocument.Load(projectPath);
        project.Root!.Add(new XElement("ItemGroup", PackageIds.Select(id => new XElement("PackageReference",
            new XAttribute("Include", id), new XAttribute("Version", PackageVersion),
            new XAttribute("GeneratePathProperty", "true"), new XAttribute("PrivateAssets", "all"), new XAttribute("IncludeAssets", "none")))));
        project.Root!.Add(new XElement("Import", new XAttribute("Project", "Build/CommandMetadata.targets")));
        project.Save(projectPath);
    }

    // XML escaping does not prevent MSBuild property expansion or item-list splitting.
    internal static string EscapeProjectLiteral(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is '%' or '$' or '@' or '(' or ')' or ';' or '\'' or '*' or '?')
                result.Append('%').Append(((int)character).ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            else
                result.Append(character);
        }
        return result.ToString();
    }

    private static string Read(string suffix)
    {
        using var stream = typeof(PowerShellCommandMetadataBuildSupport).Assembly.GetManifestResourceStream("PowerForge.PowerShell.Compilation." + suffix)
            ?? throw new InvalidOperationException("Missing command metadata build asset: " + suffix);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
