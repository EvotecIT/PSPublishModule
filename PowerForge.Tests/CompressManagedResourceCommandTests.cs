using System.IO.Compression;
using System.Management.Automation;
using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class CompressManagedResourceCommandTests
{
    [Fact]
    public void CompressManagedResource_creates_module_nupkg_and_returns_fileinfo_with_passthru()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        CreateModule(moduleRoot.Path, "Company.Tools", "1.2.3");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Compress-ManagedResource")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("DestinationPath", destination.Path)
            .AddParameter("PassThru");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var file = Assert.IsType<FileInfo>(Assert.Single(results).BaseObject);
        Assert.True(file.Exists);
        Assert.Equal("Company.Tools.1.2.3.nupkg", file.Name);

        var metadata = new ManagedModulePackageReader().ReadMetadata(file.FullName);
        Assert.Equal("Company.Tools", metadata.Id);
        Assert.Equal("1.2.3", metadata.Version);
        Assert.Contains("PSModule", metadata.Tags);

        using var archive = ZipFile.OpenRead(file.FullName);
        Assert.Contains(archive.Entries, entry => entry.FullName.Equals("Company.Tools.psd1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(archive.Entries, entry => entry.FullName.Equals("Company.Tools.psm1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompressManagedResource_writes_no_pipeline_output_without_passthru()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        CreateModule(moduleRoot.Path, "Company.Tools", "1.2.3");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Compress-ManagedResource")
            .AddParameter("Path", moduleRoot.Path)
            .AddParameter("DestinationPath", destination.Path);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        Assert.Empty(results);
        Assert.True(File.Exists(Path.Combine(destination.Path, "Company.Tools.1.2.3.nupkg")));
    }

    [Fact]
    public void CompressManagedResource_reports_script_resources_as_explicit_gap()
    {
        using var scriptRoot = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(scriptRoot.Path, "Install-CompanyTool.ps1"), "param()");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Compress-ManagedResource")
            .AddParameter("Path", scriptRoot.Path)
            .AddParameter("DestinationPath", destination.Path);

        var exception = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());

        Assert.Contains("Script resource compression is not supported yet", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFiles(destination.Path));
    }

    private static PowerShell CreatePowerShellWithModuleImported()
    {
        var ps = PowerShell.Create();
        ps.AddCommand("Import-Module")
            .AddParameter("Name", typeof(CompressManagedResourceCommand).Assembly.Location)
            .AddParameter("Force");
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();
        return ps;
    }

    private static void CreateModule(string root, string name, string version)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, name + ".psm1"), "function Get-CompanyTool { 'ok' }");
        File.WriteAllText(Path.Combine(root, name + ".psd1"), $$"""
@{
    RootModule = '{{name}}.psm1'
    ModuleVersion = '{{version}}'
    Author = 'Evotec'
    Description = 'Company tools module.'
    PrivateData = @{
        PSData = @{
            Tags = @('company', 'automation')
        }
    }
}
""");
    }

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }
}
