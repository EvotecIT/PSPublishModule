using System.Management.Automation;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class ManagedModuleAliasCommandTests
{
    [Theory]
    [InlineData("Find-PublicModule", "Find-ManagedModule")]
    [InlineData("Install-PublicModule", "Install-ManagedModule")]
    [InlineData("Publish-PublicModule", "Publish-ManagedModule")]
    [InlineData("Save-PublicModule", "Save-ManagedModule")]
    [InlineData("Update-PublicModule", "Update-ManagedModule")]
    public void Managed_module_public_aliases_resolve_to_managed_commands(string aliasName, string commandName)
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-Command")
            .AddArgument(aliasName);

        var command = Assert.IsType<AliasInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Equal(commandName, command.Definition);
    }

    [Fact]
    public void PublishManagedModule_exposes_publish_compatibility_switches()
    {
        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Get-Command")
            .AddArgument("Publish-ManagedModule");

        var command = Assert.IsType<CmdletInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(command.Parameters.ContainsKey("SkipDependenciesCheck"));
        Assert.True(command.Parameters.ContainsKey("SkipModuleManifestValidate"));
    }

    private static PowerShell CreatePowerShellWithModuleImported()
    {
        var ps = PowerShell.Create();
        ps.AddCommand("Import-Module")
            .AddParameter("Name", typeof(InstallManagedModuleCommand).Assembly.Location)
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
