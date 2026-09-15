namespace PowerForge;

public sealed partial class ModulePublisher
{
    internal static void ValidateDirectGitHubArtefactAssets(
        IReadOnlyCollection<ArtefactBuildResult> selectedArtefacts)
    {
        foreach (ArtefactBuildResult artefact in selectedArtefacts.Where(static artefact =>
                     artefact is not null && artefact.Type == ArtefactType.ScriptPacked))
        {
            if (PowerShellScriptArchiveValidator.TryValidate(
                    artefact.OutputPath,
                    artefact.EntryPointRelativePath,
                    out string? error))
            {
                continue;
            }

            throw new InvalidDataException(
                $"ScriptPacked artefact '{artefact.OutputPath}' is not safe for direct GitHub publication: {error}");
        }
    }
}
