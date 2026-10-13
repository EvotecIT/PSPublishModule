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
        var options = JsonSerializer.Deserialize<BackupOptions>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("Backup configuration is empty.");
        options.Validate();
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
        if (!Regex.IsMatch(Destination.Host, "^[a-zA-Z0-9][a-zA-Z0-9.-]{0,252}$") ||
            !Regex.IsMatch(Destination.User, "^[a-zA-Z_][a-zA-Z0-9_-]{0,31}$") || Destination.Port is < 1 or > 65535)
            throw new InvalidDataException("Invalid SSH destination.");
        if (Databases.Length == 0 || Databases.Select(d => d.Id).Distinct().Count() != Databases.Length)
            throw new InvalidDataException("Unique database sources are required.");
        foreach (var source in Databases)
        {
            if (!Regex.IsMatch(source.Id, "^[a-z][a-z0-9-]{0,31}$") || source.Provider is not ("sqlite" or "sqlserver"))
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
            if (!Regex.IsMatch(file.Id, "^[a-z][a-z0-9-]{0,31}$")) throw new InvalidDataException("Invalid recovery file id.");
            Safety.DemandAbsolute(file.Path);
            if (Safety.Overlaps(WorkRoot, file.Path)) throw new InvalidDataException("Recovery files overlap staging.");
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
