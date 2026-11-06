using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class InvokeModuleBuildPublishingTests
{
    [Fact]
    public void Run_PowerShellAuthoredBuild_ReusesPreparedStagingWithoutBuildingDotNet()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-native-staging-" + Guid.NewGuid().ToString("N")));
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample"));
            var staged = Directory.CreateDirectory(Path.Combine(root.FullName, "prepared"));
            const string manifest = "@{RootModule='Sample.psm1';ModuleVersion='1.0.0';GUID='a28d1771-0f39-4569-9404-437bb10edc34';FunctionsToExport=@();CmdletsToExport=@();AliasesToExport=@()}";
            foreach (var directory in new[] { source, staged })
            {
                File.WriteAllText(Path.Combine(directory.FullName, "Sample.psd1"), manifest);
                File.WriteAllText(Path.Combine(directory.FullName, "Sample.psm1"), "# fixture module");
            }
            File.WriteAllText(Path.Combine(source.FullName, "payload.txt"), "source payload");
            File.WriteAllText(Path.Combine(staged.FullName, "payload.txt"), "prepared payload");
            var state = InitialSessionState.CreateDefault();
            state.Commands.Add(new SessionStateCmdletEntry("Invoke-ModuleBuild", typeof(InvokeModuleBuildCommand), null));
            using var shell = PowerShell.Create(state);
            shell.AddCommand("Invoke-ModuleBuild")
                .AddParameter("ModuleName", "Sample")
                .AddParameter("Path", root.FullName)
                .AddParameter("StagingPath", staged.FullName)
                .AddParameter("NoDotnetBuild")
                .AddParameter("ReuseStaging")
                .AddParameter("PowerForgeReleaseCheckpoint")
                .AddParameter("SkipInstall")
                .AddParameter("KeepStaging")
                .AddParameter("NoInteractive")
                .AddParameter("PassThru")
                .AddParameter("ErrorAction", ActionPreference.Stop);

            var output = shell.Invoke();
            Assert.False(shell.HadErrors, string.Join(Environment.NewLine, shell.Streams.Error));
            var result = Assert.IsType<ModulePipelineResult>(Assert.Single(output).BaseObject);
            Assert.True(result.Plan.BuildSpec.ReuseStaging);
            Assert.True(result.Plan.BuildSpec.SkipDotNetBuild);
            Assert.Equal("prepared payload", File.ReadAllText(Path.Combine(staged.FullName, "payload.txt")));
            Assert.Equal("source payload", File.ReadAllText(Path.Combine(source.FullName, "payload.txt")));
        }
        finally { root.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("Modern", false, true)]
    [InlineData("Modern", true, true)]
    [InlineData("Config", false, true)]
    [InlineData("Config", true, true)]
    [InlineData("Configuration", false, null)]
    [InlineData("Configuration", true, null)]
    [InlineData("Modern", false, null)]
    [InlineData("Modern", false, false)]
    [InlineData("Config", false, null)]
    [InlineData("Config", false, false)]
    public void JsonOnly_ModulePublishingSelection_PreservesPackageLane(string inputKind, bool excludeModulePublishing, bool? noDotnetBuild)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-publish-selection-" + Guid.NewGuid().ToString("N")));
        try
        {
            var module = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample"));
            File.WriteAllText(Path.Combine(module.FullName, "Sample.psd1"), "@{ RootModule = 'Sample.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(module.FullName, "Sample.psm1"), string.Empty);
            File.WriteAllText(Path.Combine(module.FullName, "project.json"), "{ \"RootPath\": \".\" }");
            var configPath = Path.Combine(root.FullName, "input.json");
            File.WriteAllText(configPath, """
                {
                  "Build": { "Name": "Sample", "SourcePath": "Sample", "Version": "1.0.0" },
                  "Segments": [
                    { "Type": "ProjectBuild", "Configuration": { "Name": "Library", "ConfigPath": "project.json", "PublishNuget": true } },
                    { "Type": "GalleryNuget", "Configuration": { "Enabled": false, "Destination": "PowerShellGallery" } },
                    { "Type": "GitHubNuget", "Configuration": { "Enabled": false, "Destination": "GitHub" } }
                  ]
                }
                """);
            var outputPath = Path.Combine(root.FullName, "export.json");
            var state = InitialSessionState.CreateDefault();
            state.Commands.Add(new SessionStateCmdletEntry("Invoke-ModuleBuild", typeof(InvokeModuleBuildCommand), null));
            using var shell = PowerShell.Create(state);
            shell.Runspace.SessionStateProxy.Path.SetLocation(module.FullName);
            shell.AddCommand("Invoke-ModuleBuild")
                .AddParameter("RunMode", ConfigurationGateMode.Publish)
                .AddParameter("JsonOnly")
                .AddParameter("JsonPath", outputPath)
                .AddParameter("NoInteractive")
                .AddParameter("ErrorAction", ActionPreference.Stop);
            if (noDotnetBuild.HasValue)
                shell.AddParameter("NoDotnetBuild", noDotnetBuild.Value);
            if (excludeModulePublishing)
                shell.AddParameter("IncludeModulePublishing", false);

            switch (inputKind)
            {
                case "Modern":
                    shell.AddParameter("Path", root.FullName)
                        .AddParameter("ModuleName", "Sample")
                        .AddParameter("Settings", ScriptBlock.Create("""
                            [PowerForge.ConfigurationProjectBuildSegment]@{
                                Configuration = [PowerForge.ProjectBuildConfigurationReference]@{
                                    Name = 'Library'; ConfigPath = 'project.json'; PublishNuget = $true
                                }
                            }
                            [PowerForge.ConfigurationPublishSegment]@{
                                Configuration = [PowerForge.PublishConfiguration]@{ Enabled = $false; Destination = 'PowerShellGallery' }
                            }
                            [PowerForge.ConfigurationPublishSegment]@{
                                Configuration = [PowerForge.PublishConfiguration]@{ Enabled = $false; Destination = 'GitHub' }
                            }
                            """));
                    break;
                case "Config":
                    shell.AddParameter("ConfigPath", configPath);
                    break;
                case "Configuration":
                    shell.AddParameter("Configuration", new Hashtable
                    {
                        ["Information"] = new Hashtable { ["ModuleName"] = "Sample" }
                    });
                    break;
            }

            shell.Invoke();
            Assert.False(shell.HadErrors, string.Join(Environment.NewLine, shell.Streams.Error));
            var prepared = new ModulePipelineConfigurationService().Load(outputPath).Spec;
            Assert.Equal(ConfigurationGateMode.Publish, Assert.Single(prepared.Segments.OfType<ConfigurationGateSegment>()).Configuration.Mode);
            var publishes = prepared.Segments.OfType<ConfigurationPublishSegment>().ToArray();
            if (excludeModulePublishing || inputKind == "Configuration")
                Assert.Empty(publishes);
            else
                Assert.Equal(new[] { PublishDestination.PowerShellGallery, PublishDestination.GitHub }, publishes.Select(publish => publish.Configuration.Destination));

            if (inputKind != "Configuration")
            {
                Assert.Equal(noDotnetBuild == true, prepared.Build.SkipDotNetBuild);
                var project = Assert.Single(prepared.Segments.OfType<ConfigurationProjectBuildSegment>());
                Assert.Equal("Library", project.Configuration.Name);
                Assert.True(project.Configuration.PublishNuget);
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
