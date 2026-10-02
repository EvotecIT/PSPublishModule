using System.Text.Json;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebsiteArtifactExportTests
{
    [Fact]
    public void Export_ImportsModuleMetadataAndExamplesIntoProjectCatalog()
    {
        using var fixture = new ExportFixture("  Build and publish example modules.  ");
        var run = fixture.Export(skipBuild: true);
        Assert.True(run.ExitCode == 0, run.StdErr);

        var catalogPath = Path.Combine(fixture.Root, "catalog.json");
        File.WriteAllText(catalogPath, "{\"projects\":[]}");
        var pipelinePath = Path.Combine(fixture.Root, "pipeline.json");
        File.WriteAllText(pipelinePath, """
            {
              "steps": [{
                "task": "project-catalog",
                "catalog": "./catalog.json",
                "sourcesRoot": "./sources",
                "contentRoot": "./content/projects",
                "importManifests": true,
                "allowCreateProjects": true,
                "applyCuration": false,
                "mergeTelemetry": false,
                "mergeReleaseTelemetry": false,
                "generatePages": true,
                "generateSections": true
              }]
            }
            """);
        Assert.True(WebPipelineRunner.RunPipeline(pipelinePath, logger: null).Success);
        using var catalog = JsonDocument.Parse(File.ReadAllText(catalogPath));
        var project = Assert.Single(catalog.RootElement.GetProperty("projects").EnumerateArray());
        Assert.Equal("Build and publish example modules.", project.GetProperty("description").GetString());
        Assert.Equal("3.0.1", project.GetProperty("version").GetString());
        Assert.True(project.GetProperty("surfaces").GetProperty("examples").GetBoolean());
        Assert.Equal("WebsiteArtifacts/apidocs/powershell/examples", project.GetProperty("artifacts").GetProperty("examples").GetString());
        Assert.Contains("description: \"Build and publish example modules.\"", File.ReadAllText(Path.Combine(fixture.Root, "content", "projects", "pspublishmodule.md")));
        Assert.Equal("<helpItems>Current help</helpItems>", File.ReadAllText(Path.Combine(fixture.Artifacts, "apidocs", "powershell", "PSPublishModule-help.xml")));
        Assert.True(File.Exists(Path.Combine(fixture.Artifacts, "apidocs", "powershell", "examples", "Example-HTMLToPDF.ps1")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Export_UsesProductDescriptionWhenManifestDescriptionIsEmpty(string? description)
    {
        using var fixture = new ExportFixture(description);
        var run = fixture.Export(skipBuild: true);
        Assert.True(run.ExitCode == 0, run.StdErr);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.Artifacts, "project-manifest.json")));
        Assert.Equal("Build, sign, version, and publish PowerShell modules to the PowerShell Gallery.", manifest.RootElement.GetProperty("description").GetString());
    }

    [Fact]
    public void Export_RejectsUnfinishedHelpBeforeReplacingExistingArtifacts()
    {
        using var fixture = new ExportFixture("Example module");
        File.WriteAllText(fixture.HelpPath, "<helpItems>{{ Fill in the Synopsis }}</helpItems>");
        Directory.CreateDirectory(Path.Combine(fixture.Artifacts, "apidocs"));
        var existing = Path.Combine(fixture.Artifacts, "apidocs", "previous.xml");
        File.WriteAllText(existing, "Existing published help");
        var run = fixture.Export(skipBuild: true);
        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("Placeholder API content detected", run.StdErr);
        Assert.Equal("Existing published help", File.ReadAllText(existing));
        Assert.False(File.Exists(Path.Combine(fixture.Artifacts, "project-manifest.json")));
    }

    [Fact]
    public void Export_RefreshesExistingHelpUnlessBuildIsExplicitlySkipped()
    {
        using var fixture = new ExportFixture("Example module");
        File.WriteAllText(Path.Combine(fixture.Root, "Build", "Build-Project.ps1"), """
            param([switch]$ModuleOnly, [string]$RunMode)
            if (-not $ModuleOnly -or $RunMode -ne 'Documentation') { throw 'Incorrect build mode.' }
            Set-Content -LiteralPath (Join-Path $PSScriptRoot '../Module/en-US/PSPublishModule-help.xml') -Value '<helpItems>Refreshed help</helpItems>'
            """);
        var run = fixture.Export(skipBuild: false);
        Assert.True(run.ExitCode == 0, run.StdErr);
        Assert.Contains("Refreshed help", File.ReadAllText(Path.Combine(fixture.Artifacts, "apidocs", "powershell", "PSPublishModule-help.xml")));
    }

    [Fact]
    public void Export_ReadsCmdletKindsAndAliasesFromNestedCorePayload()
    {
        using var fixture = new ExportFixture("Example module");
        var setupPath = Path.Combine(fixture.Root, "Build", "Prepare-Payload.ps1");
        File.WriteAllText(setupPath, """
            $libraryRoot = Join-Path $PSScriptRoot '../Module/Lib/Core'
            New-Item -ItemType Directory -Path $libraryRoot -Force | Out-Null
            Add-Type -TypeDefinition 'using System.Management.Automation; [Cmdlet("Invoke", "ExportFixture")] public class ExportFixtureCmdlet : PSCmdlet { }' -OutputAssembly (Join-Path $libraryRoot 'PSPublishModule.dll')
            """);
        var setup = new PowerShellRunner().Run(new PowerShellRunRequest(setupPath, Array.Empty<string>(), TimeSpan.FromMinutes(1)));
        Assert.True(setup.ExitCode == 0, setup.StdErr);
        File.WriteAllText(Path.Combine(fixture.Root, "Module", "PSPublishModule.psd1"), """
            @{
                RootModule = 'PSPublishModule.psm1'
                ModuleVersion = '3.0.1'
                CmdletsToExport = @('Invoke-ExportFixture')
                FunctionsToExport = @()
                AliasesToExport = @('Build-ExportFixture')
            }
            """);
        File.WriteAllText(Path.Combine(fixture.Root, "Module", "PSPublishModule.psm1"), """
            Import-Module (Join-Path $PSScriptRoot 'Lib/Core/PSPublishModule.dll')
            Set-Alias -Name Build-ExportFixture -Value Invoke-ExportFixture
            Export-ModuleMember -Cmdlet Invoke-ExportFixture -Alias Build-ExportFixture
            """);
        var run = fixture.Export(skipBuild: true);
        Assert.True(run.ExitCode == 0, run.StdErr);
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.Artifacts, "apidocs", "powershell", "command-metadata.json")));
        var command = Assert.Single(metadata.RootElement.GetProperty("commands").EnumerateArray());
        Assert.Equal("Invoke-ExportFixture", command.GetProperty("name").GetString());
        Assert.Equal("Cmdlet", command.GetProperty("kind").GetString());
        Assert.Equal("Build-ExportFixture", Assert.Single(command.GetProperty("aliases").EnumerateArray()).GetString());
    }

    private sealed class ExportFixture : IDisposable
    {
        public string Root { get; }
        public string Artifacts => Path.Combine(Root, "sources", "pspublishmodule", "WebsiteArtifacts");
        public string HelpPath => Path.Combine(Root, "Module", "en-US", "PSPublishModule-help.xml");

        public ExportFixture(string? description)
        {
            var repo = new DirectoryInfo(AppContext.BaseDirectory);
            while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "Build", "Export-WebsiteArtifacts.ps1")))
                repo = repo.Parent;
            Assert.NotNull(repo);
            // Stay inside the checkout so the real exporter can read its Git revision.
            Root = Path.Combine(repo.FullName, "_temp", "website-export-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "Build"));
            Directory.CreateDirectory(Path.GetDirectoryName(HelpPath)!);
            File.Copy(Path.Combine(repo.FullName, "Build", "Export-WebsiteArtifacts.ps1"), Path.Combine(Root, "Build", "Export-WebsiteArtifacts.ps1"));
            var descriptionEntry = description is null ? string.Empty : $"Description = '{description.Replace("'", "''", StringComparison.Ordinal)}'";
            File.WriteAllText(Path.Combine(Root, "Module", "PSPublishModule.psd1"), "@{ ModuleVersion = '3.0.1'; " + descriptionEntry + " }");
            File.WriteAllText(HelpPath, "<helpItems>Current help</helpItems>");
            Directory.CreateDirectory(Path.Combine(Root, "Module", "Examples"));
            File.WriteAllText(Path.Combine(Root, "Module", "Examples", "Example-HTMLToPDF.ps1"), "Write-Output 'Example'");
        }

        public PowerShellRunResult Export(bool skipBuild)
        {
            var arguments = new List<string> { "-IncludeExamples", "-ArtifactsRoot", Artifacts };
            if (skipBuild) arguments.Add("-SkipBuild");
            return new PowerShellRunner().Run(new PowerShellRunRequest(
                Path.Combine(Root, "Build", "Export-WebsiteArtifacts.ps1"), arguments, TimeSpan.FromMinutes(1), workingDirectory: Root));
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
