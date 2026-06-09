using System.Management.Automation;

namespace PowerForge.Tests;

public sealed class PowerForgeProjectCmdletTests
{
    [Fact]
    public void ExportConfigurationProject_ExistingFileWithoutForce_ReturnsResourceExistsError()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var outputPath = Path.Combine(tempRoot, "project.release.json");
            File.WriteAllText(outputPath, "{}");

            var project = new ConfigurationProject
            {
                Name = "Demo",
                Targets = new[]
                {
                    new ConfigurationProjectTarget
                    {
                        Name = "Cli",
                        ProjectPath = "src/Cli/Cli.csproj",
                        Framework = "net10.0"
                    }
                }
            };

            using var ps = CreatePowerShellWithModuleImported();
            ps.AddCommand("Export-ConfigurationProject")
                .AddParameter("Project", project)
                .AddParameter("OutputPath", outputPath);

            var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
            Assert.IsType<IOException>(ex.InnerException);
            Assert.Contains("Use -Force to overwrite.", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ExportConfigurationProject_Force_OverwritesExistingFile()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var outputPath = Path.Combine(tempRoot, "project.release.json");
            File.WriteAllText(outputPath, "{}");

            var project = new ConfigurationProject
            {
                Name = "Demo",
                Targets = new[]
                {
                    new ConfigurationProjectTarget
                    {
                        Name = "Cli",
                        ProjectPath = "src/Cli/Cli.csproj",
                        Framework = "net10.0"
                    }
                }
            };

            using var ps = CreatePowerShellWithModuleImported();
            ps.AddCommand("Export-ConfigurationProject")
                .AddParameter("Project", project)
                .AddParameter("OutputPath", outputPath)
                .AddParameter("Force");

            var results = ps.Invoke();

            Assert.False(ps.HadErrors);
            Assert.Single(results);
            Assert.Equal(Path.GetFullPath(outputPath), Assert.IsType<string>(results[0].BaseObject));
            Assert.Contains("\"Name\": \"Demo\"", File.ReadAllText(outputPath), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static PowerShell CreatePowerShellWithModuleImported()
    {
        var ps = PowerShell.Create();
        ps.AddCommand("Import-Module")
            .AddParameter("Name", typeof(PSPublishModule.ExportConfigurationProjectCommand).Assembly.Location)
            .AddParameter("Force");
        _ = ps.Invoke();
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));

        ps.Commands.Clear();
        return ps;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForgeProjectCmdlets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
