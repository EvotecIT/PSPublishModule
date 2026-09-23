namespace PowerForge;

/// <summary>A reviewed task declared by JSON or discovered from a conventional build script.</summary>
public sealed class ProjectTaskPlan
{
    internal ProjectTaskPlan(string repositoryRoot, string configurationPath, string configurationSha256,
        string id, string name, string? description, string executable, IReadOnlyList<string> arguments,
        string workingDirectory, TimeSpan timeout)
    {
        RepositoryRoot = repositoryRoot;
        ConfigurationPath = configurationPath;
        ConfigurationSha256 = configurationSha256;
        Id = id;
        Name = name;
        Description = description;
        Executable = executable;
        Arguments = arguments;
        WorkingDirectory = workingDirectory;
        Timeout = timeout;
    }

    /// <summary>Working copy containing the task definition or discovered script.</summary>
    public string RepositoryRoot { get; }
    /// <summary>JSON file that declared the task, or the discovered script itself.</summary>
    public string ConfigurationPath { get; }
    /// <summary>Fingerprint of the inspected JSON file or script.</summary>
    public string ConfigurationSha256 { get; }
    /// <summary>Stable task ID.</summary>
    public string Id { get; }
    /// <summary>Operator-facing task name.</summary>
    public string Name { get; }
    /// <summary>Optional explanation of the task's intent.</summary>
    public string? Description { get; }
    /// <summary>Executable name or resolved path.</summary>
    public string Executable { get; }
    /// <summary>Structured process arguments.</summary>
    public IReadOnlyList<string> Arguments { get; }
    /// <summary>Existing working directory within the working copy.</summary>
    public string WorkingDirectory { get; }
    /// <summary>Maximum task runtime.</summary>
    public TimeSpan Timeout { get; }
}

/// <summary>The local tasks discovered for a working copy. An absent config can expose build script shortcuts.</summary>
public sealed class ProjectTaskCatalog
{
    internal ProjectTaskCatalog(string configurationPath, IReadOnlyList<ProjectTaskPlan> tasks)
    {
        ConfigurationPath = configurationPath;
        Tasks = tasks;
    }

    /// <summary>Expected JSON configuration path.</summary>
    public string ConfigurationPath { get; }
    /// <summary>Reviewed project tasks.</summary>
    public IReadOnlyList<ProjectTaskPlan> Tasks { get; }
}
