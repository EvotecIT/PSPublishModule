using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge;

/// <summary>Loads and runs explicit local project tasks through the shared structured process runner.</summary>
public sealed class ProjectTaskService
{
    /// <summary>Repository-relative location of the task contract.</summary>
    public const string ConfigurationRelativePath = "Build/powerforge.tasks.json";

    private const int MaxConfigurationBytes = 256 * 1024;
    private readonly IProcessRunner _runner;

    /// <summary>Creates the service with PowerForge's standard process runner.</summary>
    public ProjectTaskService() : this(new ProcessRunner(ownProcessTree: true)) { }

    /// <summary>Creates the service with an explicit process runner.</summary>
    public ProjectTaskService(IProcessRunner runner) => _runner = runner ?? throw new ArgumentNullException(nameof(runner));

    /// <summary>Loads and validates a working copy's tasks without starting project code.</summary>
    public ProjectTaskCatalog Load(string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot)) throw new ArgumentException("Working copy path is required.", nameof(repositoryRoot));
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Working copy does not exist: {root}");
        var path = Path.Combine(root, "Build", "powerforge.tasks.json");
        if (!File.Exists(path)) return new ProjectTaskCatalog(path, []);

        var bytes = ReadBounded(path);
        ValidateShape(bytes);
        var hash = ComputeSha256(bytes);
        var config = JsonSerializer.Deserialize<TaskConfiguration>(bytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                     ?? throw new InvalidDataException("Project task configuration is empty.");
        if (config.SchemaVersion != 1)
            throw new InvalidDataException("Project task configuration requires SchemaVersion 1.");
        if (config.Tasks is null || config.Tasks.Length == 0)
            throw new InvalidDataException("Project task configuration requires at least one task.");
        if (config.Tasks.Length > 100)
            throw new InvalidDataException("Project task configuration exceeds 100 tasks.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tasks = new List<ProjectTaskPlan>(config.Tasks.Length);
        foreach (var item in config.Tasks)
        {
            if (item is null) throw new InvalidDataException("Project task entries cannot be null.");
            if (!IsValidId(item.Id) || !ids.Add(item.Id!))
                throw new InvalidDataException("Project task IDs must be unique letters, digits, dots, dashes or underscores, up to 64 characters.");
            if (!IsValidText(item.Name, 120) || !IsValidText(item.Executable, 1024))
                throw new InvalidDataException($"Project task '{item.Id}' requires a name and executable without control characters.");
            if (item.Description is not null && !IsValidText(item.Description, 500))
                throw new InvalidDataException($"Project task '{item.Id}' description is invalid.");
            if (item.Arguments is { Length: > 100 } || item.Arguments?.Any(argument => argument is null || !IsValidArgument(argument)) == true)
                throw new InvalidDataException($"Project task '{item.Id}' has invalid arguments.");
            if (item.TimeoutSeconds is < 1 or > 21600)
                throw new InvalidDataException($"Project task '{item.Id}' timeout must be between 1 and 21600 seconds.");

            var directory = ResolveWorkingDirectory(root, item.WorkingDirectory);
            var executable = ResolveExecutable(root, item.Executable!);
            tasks.Add(new ProjectTaskPlan(root, path, hash, item.Id!, item.Name!, item.Description, executable,
                item.Arguments ?? [], directory, TimeSpan.FromSeconds(item.TimeoutSeconds ?? 600)));
        }
        return new ProjectTaskCatalog(path, tasks);
    }

    /// <summary>Runs one reviewed task, refusing a changed config immediately before process start.</summary>
    public Task<ProcessRunResult> RunAsync(ProjectTaskPlan plan, Action<string>? onOutputLine = null,
        Action<string>? onErrorLine = null, CancellationToken cancellationToken = default)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        cancellationToken.ThrowIfCancellationRequested();
        var current = Load(plan.RepositoryRoot);
        if (!FingerprintMatches(plan, current))
            throw new InvalidOperationException("Project task configuration changed since inspection. Inspect the tasks again before running one.");
        var task = current.Tasks.Single(candidate => candidate.Id.Equals(plan.Id, StringComparison.OrdinalIgnoreCase));
        var request = new ProcessRunRequest(task.Executable, task.WorkingDirectory, task.Arguments, task.Timeout,
            environmentVariables: null, captureOutput: true, captureError: true, onOutputLine, onErrorLine)
        {
            MaxCapturedOutputCharacters = 128 * 1024,
            RequireDirectStart = true
        };
        request.SetPreStartBoundary(() =>
        {
            if (!FingerprintMatches(plan, Load(plan.RepositoryRoot)))
                throw new InvalidOperationException("Project task configuration changed before process start. Inspect the tasks again.");
        });
        return _runner.RunAsync(request, cancellationToken);
    }

    private static bool FingerprintMatches(ProjectTaskPlan plan, ProjectTaskCatalog catalog)
        => catalog.Tasks.Any(task => task.Id.Equals(plan.Id, StringComparison.OrdinalIgnoreCase)
            && task.ConfigurationSha256.Equals(plan.ConfigurationSha256, StringComparison.OrdinalIgnoreCase));

    private static byte[] ReadBounded(string path)
    {
        if (new FileInfo(path).Length > MaxConfigurationBytes)
            throw new InvalidDataException("Project task configuration exceeds 256 KiB.");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > MaxConfigurationBytes)
            throw new InvalidDataException("Project task configuration exceeds 256 KiB.");
        return bytes;
    }

    private static void ValidateShape(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Project task configuration must be a JSON object.");
        ValidateMembers(document.RootElement, "configuration", ["SchemaVersion", "Tasks"]);
        var tasks = document.RootElement.EnumerateObject()
            .FirstOrDefault(property => property.Name.Equals("Tasks", StringComparison.OrdinalIgnoreCase)).Value;
        if (tasks.ValueKind == JsonValueKind.Array)
        {
            foreach (var task in tasks.EnumerateArray())
            {
                if (task.ValueKind == JsonValueKind.Object)
                    ValidateMembers(task, "task", ["Id", "Name", "Description", "Executable", "Arguments", "WorkingDirectory", "TimeoutSeconds"]);
            }
        }
    }

    private static void ValidateMembers(JsonElement element, string label, string[] allowed)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name) || !allowed.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Project task {label} has a duplicate or unsupported property '{property.Name}'.");
        }
    }

    private static string ComputeSha256(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
    }

    private static bool IsValidId(string? value)
        => value is not null && !string.IsNullOrWhiteSpace(value) && value.Length <= 64 &&
           value.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-' or '_');

    private static bool IsValidText(string? value, int maxLength)
        => value is not null && !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength && !value.Any(char.IsControl);

    private static bool IsValidArgument(string value)
        => value.Length <= 4096 && !value.Any(c => c is '\0' or '\r' or '\n');

    private static string ResolveWorkingDirectory(string root, string? relative)
    {
        if (relative is not null && !IsValidText(relative, 1024))
            throw new InvalidDataException("Project task working directory is invalid.");
        var directory = Path.GetFullPath(Path.Combine(root, relative ?? "."));
        if (Path.IsPathRooted(relative ?? ".") || !IsWithin(root, directory))
            throw new InvalidDataException("Project task working directory must stay inside its working copy.");
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Project task working directory does not exist: {directory}");
        return directory;
    }

    private static string ResolveExecutable(string root, string executable)
    {
        if (Path.IsPathRooted(executable)) return Path.GetFullPath(executable);
        if (executable.IndexOfAny(['/', '\\']) < 0) return executable;
        var path = Path.GetFullPath(Path.Combine(root, executable));
        if (!IsWithin(root, path))
            throw new InvalidDataException("A relative task executable must stay inside its working copy.");
        return path;
    }

    private static bool IsWithin(string root, string path)
    {
        var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.Equals(root, comparison) || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison);
    }

    private sealed class TaskConfiguration
    {
        public int SchemaVersion { get; set; }
        public TaskEntry?[]? Tasks { get; set; }
    }

    private sealed class TaskEntry
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Executable { get; set; }
        public string[]? Arguments { get; set; }
        public string? WorkingDirectory { get; set; }
        public int? TimeoutSeconds { get; set; }
    }
}
