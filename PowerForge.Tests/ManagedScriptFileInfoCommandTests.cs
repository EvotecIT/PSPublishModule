using System.Collections;
using System.Management.Automation;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class ManagedScriptFileInfoCommandTests
{
    [Fact]
    public void ManagedScriptFileInfo_commands_are_exported()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-Command")
            .AddParameter("Name", new[]
            {
                "New-ManagedScriptFileInfo",
                "Get-ManagedScriptFileInfo",
                "Test-ManagedScriptFileInfo",
                "Update-ManagedScriptFileInfo"
            });

        var commands = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        Assert.Equal(4, commands.Count);
    }

    [Fact]
    public void NewGetTestAndUpdateManagedScriptFileInfo_roundtrip_metadata()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Invoke-Company.ps1");
        using var ps = CreatePowerShellWithModuleImported();

        ps.AddCommand("New-ManagedScriptFileInfo")
            .AddParameter("Path", path)
            .AddParameter("Description", "Creates a company report.")
            .AddParameter("Author", "Evotec")
            .AddParameter("Tags", new[] { "company", "report" })
            .AddParameter("RequiredModules", new[]
            {
                new Hashtable
                {
                    ["ModuleName"] = "Microsoft.Graph",
                    ["RequiredVersion"] = "2.38.0"
                }
            });

        var created = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        Assert.Empty(created);
        Assert.True(File.Exists(path));

        ps.Commands.Clear();
        ps.AddCommand("Test-ManagedScriptFileInfo").AddParameter("Path", path);
        Assert.True((bool)Assert.Single(ps.Invoke()).BaseObject);
        AssertNoPowerShellErrors(ps);

        ps.Commands.Clear();
        ps.AddCommand("Update-ManagedScriptFileInfo")
            .AddParameter("Path", path)
            .AddParameter("Version", "1.2.3")
            .AddParameter("ReleaseNotes", "Updated");
        var updated = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        Assert.Empty(updated);

        ps.Commands.Clear();
        ps.AddCommand("Get-ManagedScriptFileInfo").AddParameter("Path", path);
        var read = Assert.IsType<ManagedScriptFileInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Equal("Invoke-Company", read.Name);
        Assert.Equal("1.2.3", read.Version);
        Assert.Equal(new[] { "company", "report" }, read.Tags);
        Assert.Equal("Microsoft.Graph", Assert.Single(read.RequiredModules).ModuleName);
    }

    [Fact]
    public void UpdateManagedScriptFileInfo_can_clear_tags_with_empty_array()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Invoke-Company.ps1");
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("New-ManagedScriptFileInfo")
            .AddParameter("Path", path)
            .AddParameter("Description", "Creates a company report.")
            .AddParameter("Tags", new[] { "company", "report" });
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);

        ps.Commands.Clear();
        ps.AddCommand("Update-ManagedScriptFileInfo")
            .AddParameter("Path", path)
            .AddParameter("Tags", Array.Empty<string>());
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);

        ps.Commands.Clear();
        ps.AddCommand("Get-ManagedScriptFileInfo").AddParameter("Path", path);
        var read = Assert.IsType<ManagedScriptFileInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Empty(read.Tags);
    }

    private static PowerShell CreatePowerShellWithModuleImported()
    {
        var ps = PowerShell.Create();
        ps.AddCommand("Import-Module")
            .AddParameter("Name", typeof(NewManagedScriptFileInfoCommand).Assembly.Location)
            .AddParameter("Force");
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();
        return ps;
    }

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }
}
