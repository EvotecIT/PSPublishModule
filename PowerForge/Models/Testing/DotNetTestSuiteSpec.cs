namespace PowerForge;

/// <summary>Declarative repository test profiles shared by the CLI and reusable CI.</summary>
public sealed class DotNetTestSuiteSpec
{
    /// <summary>Repository root relative to the configuration file.</summary>
    public string ProjectRoot { get; set; } = ".";
    /// <summary>Named build, test and native smoke profiles.</summary>
    public DotNetTestProfile[] Profiles { get; set; } = Array.Empty<DotNetTestProfile>();
}

/// <summary>Project policy for one test job; SDK and process setup remain shared.</summary>
public sealed class DotNetTestProfile
{
    /// <summary>Profile selected by the workflow or CLI.</summary>
    public string Name { get; set; } = "test";
    /// <summary>Build configuration.</summary>
    public string Configuration { get; set; } = "Release";
    /// <summary>Treat build warnings as errors.</summary>
    public bool WarningsAsErrors { get; set; } = true;
    /// <summary>Maximum duration of each build, test or probe process.</summary>
    public int TimeoutSeconds { get; set; } = 1200;
    /// <summary>Projects needed before the suite, such as a module or test host.</summary>
    public DotNetTestBuildProject[] BuildProjects { get; set; } = Array.Empty<DotNetTestBuildProject>();
    /// <summary>Test projects restored, built and run with separate TRX files.</summary>
    public string[] TestProjects { get; set; } = Array.Empty<string>();
    /// <summary>Native AOT projects published and executed as smoke tests.</summary>
    public DotNetTestNativeProject[] NativeProjects { get; set; } = Array.Empty<DotNetTestNativeProject>();
    /// <summary>Project-specific probes after builds, before tests. Arguments are structured, not shell text.</summary>
    public WorkspaceValidationStep[] BeforeTests { get; set; } = Array.Empty<WorkspaceValidationStep>();
    /// <summary>Project-specific probes after successful tests.</summary>
    public WorkspaceValidationStep[] AfterTests { get; set; } = Array.Empty<WorkspaceValidationStep>();
    /// <summary>Environment for this profile, including terminal locale where needed.</summary>
    public Dictionary<string, string?> EnvironmentVariables { get; set; } = new();
}

/// <summary>A prerequisite build with optional explicit frameworks.</summary>
public sealed class DotNetTestBuildProject
{
    /// <summary>Repository-relative project or solution path.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>Frameworks to build; empty uses the matrix framework, or all project frameworks.</summary>
    public string[] Frameworks { get; set; } = Array.Empty<string>();
}

/// <summary>A native publish and its foreground smoke runs.</summary>
public sealed class DotNetTestNativeProject
{
    /// <summary>Repository-relative executable project.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>Published executable filename, including .exe for Windows.</summary>
    public string Executable { get; set; } = string.Empty;
    /// <summary>Arguments for each smoke run.</summary>
    public string[] Arguments { get; set; } = Array.Empty<string>();
    /// <summary>Run once per environment overlay, or once with the profile environment when empty.</summary>
    public Dictionary<string, string?>[] RunEnvironments { get; set; } = Array.Empty<Dictionary<string, string?>>();
}
