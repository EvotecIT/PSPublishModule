namespace PowerForge.Web.Cli;

internal sealed class PowerForgeServerDurableBackup
{
    public string? ExportRoot { get; set; }
    public string? ExportGroup { get; set; }
    public string? Recipient { get; set; }
    public int StagingRetentionHours { get; set; }
    public PowerForgeServerDurableBackupDatabase[]? Databases { get; set; }
    public PowerForgeServerManagedFile[]? EncryptedFiles { get; set; }
    public PowerForgeServerDurableBackupArtifactStore[]? ArtifactStores { get; set; }
}

internal sealed class PowerForgeServerDurableBackupDatabase
{
    public string? Id { get; set; }
    public string? Provider { get; set; }
    public string? Database { get; set; }
    public bool Required { get; set; }
}

internal sealed class PowerForgeServerDurableBackupArtifactStore
{
    public string? Id { get; set; }
    public string? Path { get; set; }
    public bool Required { get; set; }
}
