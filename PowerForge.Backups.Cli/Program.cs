using PowerForge.Backups;
using System.Text.Json;

if (!((args.Length == 2 && args[0] is "run" or "validate" or "validate-config") || (args.Length == 4 && args[0] == "restore-drill")))
{
    Console.Error.WriteLine("Usage: PowerForge.Backups.Cli validate[-config] <config.json> | run <config.json> | restore-drill <config.json> <native-folder> <capture-sha256>");
    return 2;
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    var options = BackupOptions.Load(args[1]);
    if (args[0] is "validate" or "validate-config")
    {
        if (args[0] == "validate") options.ValidateCaptureInputs();
        Console.WriteLine("Backup configuration is valid.");
        return 0;
    }
    if (args[0] == "restore-drill")
    {
        await new RestoreDrill().VerifyAsync(options, args[2], args[3],
            message => Console.Error.WriteLine(message), cancellation.Token);
        Console.WriteLine("Restore drill completed successfully.");
        return 0;
    }
    var result = await new DatabaseBackupRunner().RunAsync(options,
        message => Console.Error.WriteLine(DateTimeOffset.UtcNow.ToString("O") + " " + message), cancellation.Token);
    Console.WriteLine(JsonSerializer.Serialize(result, BackupOptions.Json));
    return result.CleanupErrorType == null ? 0 : 1;
}
catch (Exception exception)
{
    // Provider exception messages can contain credentials or customer data.
    Console.Error.WriteLine("Backup failed: " + exception.GetType().Name + ". Inspect the projected run state for its stage.");
    return 1;
}
