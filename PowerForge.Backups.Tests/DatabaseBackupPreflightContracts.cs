using System.Runtime.InteropServices;
using System.Text;

namespace PowerForge.Backups.Tests;

public sealed partial class DatabaseBackupContracts
{
    [Theory]
    [InlineData("sample\n")]
    [InlineData("sample\r\n")]
    public void PortableIdentifiersRequireAnAbsoluteEnd(string id)
    {
        var options = ValidOptions(); options.Databases[0].Id = id;
        Assert.Throws<InvalidDataException>(options.Validate);
        options = ValidOptions(); options.RecoveryFiles = [new() { Id = id, Path = Path.Combine(_root, "service.json") }];
        Assert.Throws<InvalidDataException>(options.Validate);
        options = ValidOptions(); options.HostId = id;
        Assert.Throws<InvalidDataException>(options.Validate);
    }

    [Theory]
    [InlineData("queries")]
    [InlineData("query-entry")]
    [InlineData("databases")]
    [InlineData("database-entry")]
    [InlineData("recovery")]
    [InlineData("recovery-entry")]
    [InlineData("destination")]
    public void NullableJsonMembersAreRejectedBeforeAnyOutput(string member)
    {
        var options = ValidOptions();
        switch (member)
        {
            case "queries": options.Databases[0].ValidationQueries = null!; break;
            case "query-entry": options.Databases[0].ValidationQueries = [null!]; break;
            case "databases": options.Databases = null!; break;
            case "database-entry": options.Databases = [null!]; break;
            case "recovery": options.RecoveryFiles = null!; break;
            case "recovery-entry": options.RecoveryFiles = [null!]; break;
            case "destination": options.Destination = null!; break;
        }
        var config = Path.Combine(_root, "options.json");
        File.WriteAllText(config, System.Text.Json.JsonSerializer.Serialize(options, BackupOptions.Json));
        Assert.Throws<InvalidDataException>(() => BackupOptions.Load(config));
        Assert.False(Directory.Exists(options.WorkRoot));
        Assert.False(File.Exists(options.StatePath));
    }

    [Theory]
    [InlineData("ssh")]
    [InlineData("key")]
    [InlineData("known_hosts")]
    [InlineData("empty-recovery")]
    public async Task UnusableTransferInputsFailBeforeDatabaseCapture(string missing)
    {
        var options = ValidOptions();
        foreach (var file in new[] { options.Destination.SshExecutable, options.Destination.IdentityFile, options.Destination.KnownHostsFile })
            File.WriteAllText(file, "synthetic input");
        if (missing == "empty-recovery")
        {
            var recovery = Path.Combine(_root, "service.json"); File.WriteAllText(recovery, "");
            options.RecoveryFiles = [new() { Id = "service", Path = recovery }];
        }
        else File.Delete(Path.Combine(_root, missing));
        var stages = new List<string>();
        var failure = await Record.ExceptionAsync(() => new DatabaseBackupRunner().RunAsync(options, stages.Add));
        if (missing == "empty-recovery") Assert.IsType<InvalidDataException>(failure);
        else Assert.IsType<FileNotFoundException>(failure);
        Assert.Equal(["preflight"], stages);
        Assert.Empty(Directory.EnumerateDirectories(options.WorkRoot));
        using var status = System.Text.Json.JsonDocument.Parse(File.ReadAllText(options.StatePath));
        Assert.Equal("failed", status.RootElement.GetProperty("status").GetString());
        Assert.Equal("preflight", status.RootElement.GetProperty("stage").GetString());
    }

    [Fact]
    public async Task WindowsShortNamesCannotHideAnInputFromStateSeparation()
    {
        if (!OperatingSystem.IsWindows()) return;
        var options = ValidOptions();
        var target = Path.Combine(_root, "protected-database-with-a-long-name.sqlite");
        File.WriteAllText(target, "preserve the physical input");
        var shortName = new StringBuilder(32768);
        Assert.NotEqual(0u, GetShortPathName(target, shortName, (uint)shortName.Capacity));
        options.StatePath = target;
        options.Databases[0].Database = shortName.ToString();
        // Also covers filesystems with 8.3 generation disabled: the identical spelling remains forbidden.
        await Assert.ThrowsAsync<InvalidDataException>(() => new DatabaseBackupRunner().RunAsync(options));
        Assert.Equal("preserve the physical input", File.ReadAllText(target));
        Assert.True(Safety.Overlaps(Path.Combine(_root, "missing-directory"), Path.Combine(Path.GetDirectoryName(shortName.ToString())!, "missing-directory", "new-file")));
        Assert.False(Directory.Exists(options.WorkRoot));
    }

    [Fact]
    public void WindowsShortAncestorNamesCannotHideNotYetCreatedOutputs()
    {
        if (!OperatingSystem.IsWindows()) return;
        var existing = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var shortName = new StringBuilder(32768);
        Assert.NotEqual(0u, GetShortPathName(existing, shortName, (uint)shortName.Capacity));
        if (string.Equals(existing, shortName.ToString(), StringComparison.OrdinalIgnoreCase)) return; // 8.3 disabled at volume creation.
        var name = "PowerForgeAliasProof-" + Guid.NewGuid().ToString("N");
        var options = ValidOptions(); options.WorkRoot = Path.Combine(existing, name);
        options.Databases[0].Database = Path.Combine(shortName.ToString(), name, "live.sqlite");
        // The supported .NET Windows runtime already expands existing DOS aliases, even with a missing tail.
        Assert.Equal(Path.GetFullPath(options.WorkRoot), Path.GetDirectoryName(Path.GetFullPath(options.Databases[0].Database)));
        Assert.Throws<InvalidDataException>(options.Validate);
        Assert.False(Directory.Exists(options.WorkRoot)); // No writes to Program Files are used by this proof.
        Assert.False(File.Exists(options.StatePath));
    }

    [Theory]
    [InlineData(".")]
    [InlineData(" ")]
    public async Task WindowsNormalizedFilenameAliasesCannotHideAProtectedInput(string suffix)
    {
        if (!OperatingSystem.IsWindows()) return;
        var options = ValidOptions(); var target = Path.Combine(_root, "protected-input");
        File.WriteAllText(target, "preserve the input");
        options.StatePath = target; options.RecoveryFiles = [new() { Id = "service", Path = target + suffix }];
        await Assert.ThrowsAsync<InvalidDataException>(() => new DatabaseBackupRunner().RunAsync(options));
        Assert.Equal("preserve the input", File.ReadAllText(target));
        Assert.False(Directory.Exists(options.WorkRoot));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathName(string longPath, StringBuilder shortPath, uint capacity);
}
