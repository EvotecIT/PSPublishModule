using System.Management.Automation;
using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class ManagedModuleCatalogCommandTests
{
    private static readonly string[] CatalogCmdlets =
    {
        "Get-ManagedModuleCatalog",
        "Set-ManagedModuleCatalog",
        "Update-ManagedModuleCatalog"
    };

    [Fact]
    public void SetAndGetManagedModuleCatalog_round_trip_through_cmdlets()
    {
        using var temp = new TemporaryDirectory();
        var catalogPath = Path.Combine(temp.Path, "catalog.json");
        var previous = Environment.GetEnvironmentVariable("POWERFORGE_MODULE_CATALOG_PATH");
        try
        {
            Environment.SetEnvironmentVariable("POWERFORGE_MODULE_CATALOG_PATH", catalogPath);
            using var ps = CreatePowerShellWithModuleImported();

            ps.AddCommand("Set-ManagedModuleCatalog")
                .AddParameter("Name", "PSGallery")
                .AddParameter("Mode", ManagedModuleCatalogCacheMode.Fallback)
                .AddParameter("MaxStaleness", TimeSpan.FromDays(5))
                .AddParameter("IncludePrerelease", false);
            var setResults = ps.Invoke();
            AssertNoPowerShellErrors(ps);
            var saved = Assert.IsType<ManagedModuleCatalog>(Assert.Single(setResults).BaseObject);
            Assert.Equal("PSGallery", saved.Name);
            Assert.Equal(ManagedModuleCatalogCacheMode.Fallback, saved.Mode);

            ps.Commands.Clear();
            ps.AddCommand("Get-ManagedModuleCatalog")
                .AddParameter("Name", "PSGallery");
            var getResults = ps.Invoke();
            AssertNoPowerShellErrors(ps);
            var loaded = Assert.IsType<ManagedModuleCatalog>(Assert.Single(getResults).BaseObject);
            Assert.Equal(TimeSpan.FromDays(5), loaded.MaxStaleness);
            Assert.False(loaded.IncludePrerelease);
        }
        finally
        {
            Environment.SetEnvironmentVariable("POWERFORGE_MODULE_CATALOG_PATH", previous);
        }
    }

    [Fact]
    public void ManagedModuleCatalog_cmdlets_are_exported_from_packaged_module_lists()
    {
        var repositoryRoot = FindRepositoryRoot();
        var manifest = File.ReadAllText(Path.Combine(repositoryRoot, "Module", "PSPublishModule.psd1"));
        var rootModule = File.ReadAllText(Path.Combine(repositoryRoot, "Module", "PSPublishModule.psm1"));

        foreach (var cmdlet in CatalogCmdlets)
        {
            Assert.Contains($"'{cmdlet}'", manifest, StringComparison.Ordinal);
            Assert.Contains($"'{cmdlet}'", rootModule, StringComparison.Ordinal);
        }
    }

    private static PowerShell CreatePowerShellWithModuleImported()
    {
        var ps = PowerShell.Create();
        ps.AddCommand("Import-Module")
            .AddParameter("Name", typeof(SetManagedModuleCatalogCommand).Assembly.Location)
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PSPublishModule.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }
}
