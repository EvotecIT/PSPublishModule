using System.Diagnostics;

namespace PowerForge.Backups.Tests;

public sealed partial class DatabaseBackupContracts
{
    [Theory]
    [InlineData("recovery")]
    [InlineData("sqlite")]
    [InlineData("configuration")]
    public async Task LinkedInputsAreRejectedBeforeAStateWriteCanReplaceTheirPhysicalTarget(string inputKind)
    {
        var options = ValidOptions();
        var physical = Path.Combine(_root, "physical"); Directory.CreateDirectory(physical);
        var alias = Path.Combine(_root, "alias");
        CreateDirectoryAlias(alias, physical);
        try
        {
            var target = Path.Combine(physical, "protected-input");
            options.StatePath = target;
            if (inputKind == "recovery") options.RecoveryFiles = [new() { Id = "service", Path = Path.Combine(alias, "protected-input") }];
            if (inputKind == "sqlite") options.Databases[0].Database = Path.Combine(alias, "protected-input");
            var original = inputKind == "configuration"
                ? System.Text.Json.JsonSerializer.Serialize(options, BackupOptions.Json)
                : "preserve the physical input";
            File.WriteAllText(target, original);
            if (inputKind == "configuration")
                Assert.Throws<IOException>(() => BackupOptions.Load(Path.Combine(alias, "protected-input")));
            else
                await Assert.ThrowsAsync<IOException>(() => new DatabaseBackupRunner().RunAsync(options));
            Assert.Equal(original, File.ReadAllText(target));
            Assert.False(Directory.Exists(options.WorkRoot));
            Assert.Equal([target], Directory.GetFiles(physical));
        }
        finally { Directory.Delete(alias); }
    }

    [Fact]
    public async Task LinkedSelectedRestoreFolderIsRejectedBeforeWritingItsPhysicalReceipt()
    {
        var options = ValidOptions();
        var physical = Path.Combine(_root, "download"); Directory.CreateDirectory(physical);
        var alias = Path.Combine(_root, "download-alias");
        CreateDirectoryAlias(alias, physical);
        try
        {
            options.StatePath = Path.Combine(physical, "state.json");
            var receipt = Path.Combine(physical, "restore-drill.json");
            File.WriteAllText(receipt, "preserve selected recovery files");
            await Assert.ThrowsAsync<IOException>(() => new RestoreDrill().VerifyAsync(options, alias, new string('0', 64)));
            Assert.Equal("preserve selected recovery files", File.ReadAllText(receipt));
            Assert.False(Directory.Exists(options.WorkRoot));
            Assert.False(File.Exists(options.StatePath));
            Assert.Equal([receipt], Directory.GetFiles(physical));
        }
        finally { Directory.Delete(alias); }
    }

    private static void CreateDirectoryAlias(string alias, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(alias, target);
            return;
        }
        // Junctions exercise Windows reparse traversal without developer mode or administrator rights.
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{alias}\" \"{target}\"")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        })!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, output + error);
    }
}
