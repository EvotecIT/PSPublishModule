using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Backups;

/// <summary>Machine-local configuration for a consistent native database backup set.</summary>
public sealed class BackupOptions
{
    public string HostId { get; set; } = "";
    public string WorkRoot { get; set; } = "";
    public string StatePath { get; set; } = "";
    public int TimeoutMinutes { get; set; } = 240;
    public long MinimumFreeBytes { get; set; } = 150L * 1024 * 1024 * 1024;
    public SshDestination Destination { get; set; } = new();
    public DatabaseSource[] Databases { get; set; } = [];
    public RecoveryFile[] RecoveryFiles { get; set; } = [];

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Loads configuration without logging connection strings or credentials.</summary>
    public static BackupOptions Load(string path)
    {
        Safety.DemandPhysical(path);
        var options = JsonSerializer.Deserialize<BackupOptions>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("Backup configuration is empty.");
        options.Validate();
        options.DemandSeparatedStorage([Path.GetFullPath(path)]);
        return options;
    }

    public void Validate()
    {
        if (!Regex.IsMatch(HostId, "^[a-z0-9][a-z0-9-]{0,63}$"))
            throw new InvalidDataException("A portable host id is required.");
        if (TimeoutMinutes is < 1 or > 1440 || MinimumFreeBytes < 0)
            throw new InvalidDataException("Invalid time or space budget.");
        foreach (var path in new[] { WorkRoot, StatePath, Destination.IdentityFile, Destination.KnownHostsFile, Destination.SshExecutable })
            Safety.DemandAbsolute(path);
        if (Path.GetFullPath(WorkRoot).TrimEnd(Path.DirectorySeparatorChar) == Path.GetPathRoot(WorkRoot)!.TrimEnd(Path.DirectorySeparatorChar))
            throw new InvalidDataException("Staging requires a dedicated directory, not a volume root.");
        if (!Regex.IsMatch(Destination.Host, "^[a-zA-Z0-9][a-zA-Z0-9.-]{0,252}$") ||
            !Regex.IsMatch(Destination.User, "^[a-zA-Z_][a-zA-Z0-9_-]{0,31}$") || Destination.Port is < 1 or > 65535)
            throw new InvalidDataException("Invalid SSH destination.");
        if (Databases.Length == 0 || Databases.Select(d => d.Id).Distinct().Count() != Databases.Length)
            throw new InvalidDataException("Unique database sources are required.");
        foreach (var source in Databases)
        {
            if (!IsPortableFileId(source.Id) || source.Provider is not ("sqlite" or "sqlserver"))
                throw new InvalidDataException("Invalid database id or provider.");
            if (source.Provider == "sqlite")
            {
                Safety.DemandAbsolute(source.Database);
                if (Safety.Overlaps(WorkRoot, source.Database))
                    throw new InvalidDataException("Backup staging must not overlap a source database.");
            }
            else if (string.IsNullOrWhiteSpace(source.ConnectionString) || string.IsNullOrWhiteSpace(source.Database))
                throw new InvalidDataException("SQL Server source requires connection and database.");
        }
        if (RecoveryFiles.Select(f => f.Id).Distinct().Count() != RecoveryFiles.Length)
            throw new InvalidDataException("Recovery file ids must be unique.");
        foreach (var file in RecoveryFiles)
        {
            if (!IsPortableFileId(file.Id)) throw new InvalidDataException("Invalid recovery file id.");
            Safety.DemandAbsolute(file.Path);
            if (Safety.Overlaps(WorkRoot, file.Path)) throw new InvalidDataException("Recovery files overlap staging.");
        }
        DemandSeparatedStorage([]);
    }

    private static bool IsPortableFileId(string id) => Regex.IsMatch(id, "^[a-z][a-z0-9-]{0,31}$") &&
        !Regex.IsMatch(id, "^(con|prn|aux|nul|com[1-9]|lpt[1-9])$");

    /// <summary>Rejects output aliases before a runner or restore drill can write to protected inputs.</summary>
    internal void DemandSeparatedStorage(IEnumerable<string> additionalInputs)
    {
        Safety.DemandPhysical(WorkRoot);
        var statusDirectory = Path.GetDirectoryName(StatePath)!;
        var outputs = new[] { StatePath, Path.Combine(statusDirectory, "last-success.json"),
            Path.Combine(statusDirectory, "restore-drill.json"), Path.Combine(statusDirectory, "restore-drill-success.json") };
        for (var i = 0; i < outputs.Length; i++)
        {
            Safety.DemandPhysical(outputs[i]);
            if (Safety.Overlaps(WorkRoot, outputs[i]) || outputs.Take(i).Any(other => Safety.Overlaps(other, outputs[i])))
                throw new InvalidDataException("Durable state outputs must be distinct and outside staging.");
        }
        var inputs = Databases.Where(d => d.Provider == "sqlite")
            .SelectMany(d => new[] { d.Database, d.Database + "-wal", d.Database + "-shm", d.Database + "-journal" })
            .Concat(RecoveryFiles.Select(f => f.Path))
            .Concat([Destination.IdentityFile, Destination.KnownHostsFile, Destination.SshExecutable])
            .Concat(additionalInputs);
        foreach (var input in inputs)
        {
            Safety.DemandPhysical(input);
            if (Safety.Overlaps(WorkRoot, input) || outputs.Any(output => Safety.Overlaps(output, input)))
                throw new InvalidDataException("Backup outputs overlap a protected input.");
        }
    }
}

public sealed class SshDestination
{
    public string Host { get; set; } = "";
    public string User { get; set; } = "root";
    public int Port { get; set; } = 22;
    public string IdentityFile { get; set; } = "";
    public string KnownHostsFile { get; set; } = "";
    public string SshExecutable { get; set; } = "";
}

public sealed class DatabaseSource
{
    public string Id { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Database { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public string[] ValidationQueries { get; set; } = [];
}

public sealed class RecoveryFile
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
}
