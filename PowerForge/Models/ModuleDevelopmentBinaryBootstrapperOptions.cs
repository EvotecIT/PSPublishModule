namespace PowerForge;

internal sealed class ModuleDevelopmentBinaryBootstrapperOptions
{
    public ModuleDevelopmentBinaryBootstrapperOptions(
        ModuleDevelopmentBinaryMode mode,
        string binaryRootPath,
        string environmentVariable,
        string configurationEnvironmentVariable,
        string[] coreFrameworkCandidates,
        string[] desktopFrameworkCandidates)
    {
        Mode = mode;
        BinaryRootPath = binaryRootPath;
        EnvironmentVariable = environmentVariable;
        ConfigurationEnvironmentVariable = configurationEnvironmentVariable;
        CoreFrameworkCandidates = coreFrameworkCandidates;
        DesktopFrameworkCandidates = desktopFrameworkCandidates;
    }

    public ModuleDevelopmentBinaryMode Mode { get; }

    public string BinaryRootPath { get; }

    public string EnvironmentVariable { get; }

    public string ConfigurationEnvironmentVariable { get; }

    public string[] CoreFrameworkCandidates { get; }

    public string[] DesktopFrameworkCandidates { get; }

    public bool Enabled => Mode != ModuleDevelopmentBinaryMode.Off;
}
