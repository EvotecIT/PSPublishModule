namespace PowerForge;

/// <summary>Loads the typed publish contract shared by JSON callers and PowerShell authoring.</summary>
public static class DotNetPublishConfiguration
{
    /// <summary>Loads a publish configuration or the tools lane of a unified release configuration, resolving its project root against the defining file.</summary>
    public static DotNetPublishSpec Load(string path)
    {
        var loaded = DotNetPublishReleaseArtifactVerifier.ReadConfiguredPublishSpecWithInputs(path);
        var configuration = loaded.Configuration;
        var directory = Path.GetDirectoryName(loaded.InputPaths.Last())!;
        configuration.DotNet.ProjectRoot = Path.GetFullPath(Path.Combine(directory, configuration.DotNet.ProjectRoot ?? "."));
        return configuration;
    }
}
