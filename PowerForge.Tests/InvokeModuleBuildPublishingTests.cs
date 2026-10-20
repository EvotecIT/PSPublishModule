using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class InvokeModuleBuildPublishingTests
{
    [Theory]
    [InlineData("Modern", false)]
    [InlineData("Modern", true)]
    [InlineData("Config", false)]
    [InlineData("Config", true)]
    [InlineData("Configuration", false)]
    [InlineData("Configuration", true)]
    public void JsonOnly_ModulePublishingSelection_PreservesPackageLane(string inputKind, bool excludeModulePublishing)
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
