using System.Management.Automation;
using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class ManagedModuleClobberCommandTests
{
    [Fact]
    public void InstallManagedModule_rejects_export_conflicts_by_default()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Existing", "1.0.0", "Get-CompanyTool");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("Company.Tools", "1.0.0", "Get-CompanyTool"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("ModuleRoot", moduleRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0");

        var exception = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());

        Assert.Contains("export conflict", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AllowClobber", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void InstallManagedModule_allows_export_conflicts_with_allow_clobber()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Existing", "1.0.0", "Get-CompanyTool");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("Company.Tools", "1.0.0", "Get-CompanyTool"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("ModuleRoot", moduleRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AllowClobber");

        var result = Assert.IsType<ManagedModuleInstallResult>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.Equal(ManagedModuleInstallStatus.Installed, result.Status);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public void InstallManagedModule_rejects_wildcard_export_clobber_risk_by_default()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        CreateInstalledModule(moduleRoot.Path, "Company.Existing", "1.0.0", "Get-CompanyTool");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: CreateModuleFiles("Company.Tools", "1.0.0", "*"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedModule")
            .AddParameter("Name", "Company.Tools")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("ModuleRoot", moduleRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0");

        var exception = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());

        Assert.Contains("wildcard export clobber risk", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AllowClobber", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
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

    private static void CreateInstalledModule(string moduleRoot, string name, string version, string functionName)
    {
        var modulePath = Path.Combine(moduleRoot, name, version);
        Directory.CreateDirectory(modulePath);
        File.WriteAllText(Path.Combine(modulePath, name + ".psd1"), CreateManifest(version, functionName));
    }

    private static IReadOnlyDictionary<string, string> CreateModuleFiles(string name, string version, string functionName)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [name + ".psd1"] = CreateManifest(version, functionName)
        };

    private static string CreateManifest(string version, string functionName)
        => "@{" + Environment.NewLine +
           "    ModuleVersion = '" + version + "'" + Environment.NewLine +
           "    FunctionsToExport = @('" + functionName + "')" + Environment.NewLine +
           "    CmdletsToExport = @()" + Environment.NewLine +
           "    AliasesToExport = @()" + Environment.NewLine +
           "}";

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }
}
