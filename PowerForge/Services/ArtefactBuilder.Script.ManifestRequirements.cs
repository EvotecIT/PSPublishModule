namespace PowerForge;

public sealed partial class ArtefactBuilder
{
    private static string CreateScriptManifestRuntimePreamble(string manifestPath)
    {
        var requirements = new List<string>();
        string? powerShellVersion = ModuleManifestValueReader.ReadTopLevelLiteralStringOrThrow(
            manifestPath,
            "PowerShellVersion",
            "Script and ScriptPacked runtime requirement preservation");
        if (!string.IsNullOrWhiteSpace(powerShellVersion))
        {
            if (!Version.TryParse(powerShellVersion, out Version? parsedVersion))
            {
                throw new InvalidOperationException(
                    $"Script artefacts cannot preserve invalid manifest PowerShellVersion '{powerShellVersion}'.");
            }

            requirements.Add("#requires -Version " + parsedVersion);
        }

        string[] compatibleEditions = (ModuleManifestValueReader.ReadTopLevelLiteralStringOrArrayOrThrow(
                                           manifestPath,
                                           "CompatiblePSEditions",
                                           "Script and ScriptPacked runtime requirement preservation") ?? Array.Empty<string>())
            .Where(static edition => !string.IsNullOrWhiteSpace(edition))
            .Select(static edition => edition.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (compatibleEditions.Length == 1)
        {
            string edition = compatibleEditions[0];
            if (!string.Equals(edition, "Core", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(edition, "Desktop", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Script artefacts cannot preserve unsupported CompatiblePSEditions value '{edition}'.");
            }

            requirements.Add("#requires -PSEdition " +
                             (string.Equals(edition, "Core", StringComparison.OrdinalIgnoreCase)
                                 ? "Core"
                                 : "Desktop"));
        }
        else if (compatibleEditions.Length > 0 &&
                 !(compatibleEditions.Length == 2 &&
                   compatibleEditions.Contains("Core", StringComparer.OrdinalIgnoreCase) &&
                   compatibleEditions.Contains("Desktop", StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Script artefacts cannot preserve CompatiblePSEditions values other than Core, Desktop, or both.");
        }

        foreach (string key in new[]
                 {
                     "PowerShellHostName",
                     "PowerShellHostVersion",
                     "DotNetFrameworkVersion",
                     "CLRVersion"
                 })
        {
            string? value = ModuleManifestValueReader.ReadTopLevelLiteralStringOrThrow(
                manifestPath,
                key,
                "Script and ScriptPacked runtime requirement preservation");
            if (!string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Script artefacts cannot preserve manifest runtime requirement '{key}'. " +
                    "Remove it or keep this output as a module package.");
            }
        }

        string? processorArchitecture = ModuleManifestValueReader.ReadTopLevelLiteralStringOrThrow(
            manifestPath,
            "ProcessorArchitecture",
            "Script and ScriptPacked runtime requirement preservation");
        if (!string.IsNullOrWhiteSpace(processorArchitecture) &&
            !string.Equals(processorArchitecture, "None", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Script artefacts cannot preserve manifest runtime requirement 'ProcessorArchitecture = {processorArchitecture}'. " +
                "Remove it or keep this output as a module package.");
        }

        return string.Join(Environment.NewLine, requirements);
    }
}
