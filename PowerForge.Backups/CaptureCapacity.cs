using DBAClientX;

namespace PowerForge.Backups;

/// <summary>Conservative staging estimates for native captures, including growth and backup metadata.</summary>
internal static class CaptureCapacity
{
    internal static long WithHeadroom(long bytes)
    {
        if (bytes <= 0) throw new InvalidDataException("Source database size could not be established.");
        return checked(bytes + bytes / 8 + 64L * 1024 * 1024);
    }

    internal static async Task<long> EstimateAsync(DatabaseSource source, CancellationToken token)
    {
        long bytes;
        if (source.Provider == "sqlite")
        {
            Safety.DemandPhysical(source.Database);
            bytes = new FileInfo(source.Database).Length;
            // WAL pages may not yet be reflected in the main file's length.
            var wal = source.Database + "-wal";
            Safety.DemandPhysical(wal);
            if (File.Exists(wal)) bytes = checked(bytes + new FileInfo(wal).Length);
        }
        else
        {
            using var client = new SqlServer();
            // DbaClientX owns connection/provider execution. The capture policy budgets allocated data
            // and log files, rather than assuming that a previous compressed backup has the same size.
            var database = source.Database.Replace("]", "]]", StringComparison.Ordinal);
            var size = await client.ExecuteScalarAsync(source.ConnectionString,
                $"USE [{database}]; SELECT CASE WHEN COUNT(*) = COUNT(CASE WHEN type IN (0,1) THEN 1 END) " +
                "THEN SUM(CONVERT(bigint,size)) * 8192 END FROM sys.database_files;", cancellationToken: token);
            if (size is null or DBNull) throw new InvalidDataException("Capture sizing requires SQL Server data and log files.");
            bytes = Convert.ToInt64(size);
        }
        return WithHeadroom(bytes);
    }

    internal static async Task<long> EstimateSetAsync(BackupOptions options, CancellationToken token)
    {
        long bytes = 1024 * 1024; // Bounded manifest plus local control files.
        foreach (var source in options.Databases)
            bytes = checked(bytes + await EstimateAsync(source, token));
        foreach (var file in options.RecoveryFiles)
            bytes = checked(bytes + new FileInfo(file.Path).Length);
        return bytes;
    }
}
