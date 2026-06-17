using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using PowerForge;
using PSPublishModule;
using Xunit;

namespace PowerForge.Tests;

public class ModuleDocumentationReviewRegressionTests
{
    [Fact]
    public void GetModuleDocumentation_Renders_Remote_File_Items_From_Content()
    {
        Assert.False(GetModuleDocumentationCommand.ShouldRenderAsLocalFile(new DocumentItem
        {
            Kind = "FILE",
            Path = "docs/guide.md",
            Content = "# Remote guide",
            Source = "Remote"
        }));

        Assert.True(GetModuleDocumentationCommand.ShouldRenderAsLocalFile(new DocumentItem
        {
            Kind = "FILE",
            Path = Path.Combine(Path.GetTempPath(), "guide.md")
        }));
    }

    [Fact]
    public void ShowModuleDocumentation_Treats_Scalar_Repository_Paths_As_Single_Value()
    {
        var repository = PSObject.AsPSObject(new Hashtable
        {
            ["Paths"] = "docs"
        });

        var paths = ShowModuleDocumentationCommand.GetPsObjectStringArrayForTesting(repository, "Paths");

        Assert.Equal(new[] { "docs" }, paths);
    }

    [Fact]
    public void InstallModuleScript_Empty_Include_Matches_All_And_Empty_Exclude_Matches_None()
    {
        var root = Path.Combine(Path.GetTempPath(), "PFScriptRoot");
        var fullPath = Path.Combine(root, "Repair-Thing.ps1");

        Assert.True(InstallModuleScriptCommand.MatchesAnyForTesting(fullPath, root, System.Array.Empty<string>(), emptyMatches: true));
        Assert.False(InstallModuleScriptCommand.MatchesAnyForTesting(fullPath, root, System.Array.Empty<string>(), emptyMatches: false));
        Assert.True(InstallModuleScriptCommand.MatchesAnyForTesting(fullPath, root, new[] { "Repair-*" }, emptyMatches: false));
    }

    [Fact]
    public void InstallModuleScript_Merge_Force_Reports_Overwrite_For_Existing_Targets()
    {
        Assert.Equal("Keep", InstallModuleScriptCommand.ResolveExistingScriptActionForTesting(true, OnExistsOption.Merge, force: false));
        Assert.Equal("Overwrite", InstallModuleScriptCommand.ResolveExistingScriptActionForTesting(true, OnExistsOption.Merge, force: true));
        Assert.Equal("Overwrite", InstallModuleScriptCommand.ResolveExistingScriptActionForTesting(true, OnExistsOption.Refresh, force: false));
    }

    [Fact]
    public void DocumentationInstaller_Refresh_OverwritesPackageFiles_AndPreservesLocalExtras()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PFDocsRefresh", System.Guid.NewGuid().ToString("N")));
        try
        {
            var internals = Directory.CreateDirectory(Path.Combine(root.FullName, "Internals", "Config"));
            File.WriteAllText(Path.Combine(internals.FullName, "config.sample.json"), "package-new");
            File.WriteAllText(Path.Combine(root.FullName, "README.md"), "readme-new");

            var destination = Directory.CreateDirectory(Path.Combine(root.FullName, "Destination"));
            var destinationConfig = Directory.CreateDirectory(Path.Combine(destination.FullName, "Config"));
            File.WriteAllText(Path.Combine(destinationConfig.FullName, "config.sample.json"), "package-old");
            File.WriteAllText(Path.Combine(destinationConfig.FullName, "config.json"), "local-config");
            File.WriteAllText(Path.Combine(destination.FullName, "README.md"), "readme-old");
            File.WriteAllText(Path.Combine(destination.FullName, "local-only.txt"), "local-extra");

            var installer = new DocumentationInstaller();
            installer.Install(root.FullName, "Demo", "1.0.0", destination.FullName, OnExistsOption.Refresh, force: false, open: false, noIntro: true);

            Assert.Equal("package-new", File.ReadAllText(Path.Combine(destinationConfig.FullName, "config.sample.json")));
            Assert.Equal("local-config", File.ReadAllText(Path.Combine(destinationConfig.FullName, "config.json")));
            Assert.Equal("readme-new", File.ReadAllText(Path.Combine(destination.FullName, "README.md")));
            Assert.Equal("local-extra", File.ReadAllText(Path.Combine(destination.FullName, "local-only.txt")));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void DocumentationPlanner_Links_Selector_Returns_Only_Configured_Links()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PFDocs", System.Guid.NewGuid().ToString("N")));
        File.WriteAllText(Path.Combine(root.FullName, "README.md"), "# Readme");

        var planner = new DocumentationPlanner(new DocumentationFinder());
        var result = planner.Execute(new DocumentationPlanner.Request
        {
            RootBase = root.FullName,
            Links = true,
            Delivery = new Hashtable
            {
                ["ImportantLinks"] = new[]
                {
                    new Dictionary<string, string>
                    {
                        ["Name"] = "Project",
                        ["Link"] = "https://example.test/project"
                    }
                }
            }
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("FILE", item.Kind);
        Assert.Contains("# Links", item.Content);
        Assert.Contains("[Project](https://example.test/project)", item.Content);
    }

    [Fact]
    public void DocumentationPlanner_Fences_AboutTopic_PowerShell_Prompt_Blocks()
    {
        var markdown = DocumentationPlanner.AboutToMarkdownForTesting("""
        EXAMPLES

        PS> Invoke-ModuleBuild -ModuleName 'MyModule' -Path . -Settings {

            >>     New-ConfigurationModule -Type RequiredModule -Name 'Pester'
            >>     New-ConfigurationBuild -Enable
            >> }

        Builds the module.
        """);

        Assert.Contains("```powershell", markdown);
        Assert.Contains("PS> Invoke-ModuleBuild -ModuleName 'MyModule' -Path . -Settings {", markdown);
        Assert.Contains(">>     New-ConfigurationModule -Type RequiredModule -Name 'Pester'", markdown);
        Assert.Contains(">> }", markdown);
        Assert.Contains("```\n\nBuilds the module.", markdown.Replace("\r\n", "\n"));
    }

    [Fact]
    public void DocumentationPlanner_Strips_Converted_AboutTopic_FrontMatter_With_Windows_LineEndings()
    {
        var markdown = DocumentationPlanner.AboutToMarkdownForTesting("TOPIC\r\n    about_Demo\r\n\r\nSHORT DESCRIPTION\r\n    Demo topic.\r\n");

        Assert.DoesNotContain("schema:", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("---", markdown, StringComparison.Ordinal);
        Assert.Contains("# about_Demo", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentationPlanner_Uses_AboutTopic_FileName_When_Topic_Section_Is_Missing()
    {
        var markdown = DocumentationPlanner.AboutToMarkdownForTesting("""
        SHORT DESCRIPTION
            Demo topic.
        """, "about_FileFallback.help.txt");

        Assert.Contains("# about_FileFallback", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("# about_topic", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentationPlanner_Discovers_Help_About_Source_Folder()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PFDocsAbout", System.Guid.NewGuid().ToString("N")));
        var about = Directory.CreateDirectory(Path.Combine(root.FullName, "Help", "About"));
        File.WriteAllText(Path.Combine(about.FullName, "about_Demo.help.txt"), """
        TOPIC
            about_Demo

        SHORT DESCRIPTION
            Demo about topic.
        """);

        try
        {
            var planner = new DocumentationPlanner(new DocumentationFinder());
            var result = planner.Execute(new DocumentationPlanner.Request
            {
                RootBase = root.FullName
            });

            var item = Assert.Single(result.Items, i => i.Kind == "ABOUT");
            Assert.Equal("about_Demo.help.txt", item.FileName);
            Assert.Contains("Demo about topic.", item.Content);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void CoreResolver_Allows_Module_Local_Microsoft_And_System_Package_Names()
    {
        Assert.False(OnModuleImportAndRemove.ShouldSkipCoreResolutionForTesting("Microsoft.Extensions.Logging"));
        Assert.False(OnModuleImportAndRemove.ShouldSkipCoreResolutionForTesting("System.Text.Json"));
        Assert.True(OnModuleImportAndRemove.ShouldSkipCoreResolutionForTesting("System.Management.Automation"));
    }

    [Fact]
    public void GetHelpParser_Uses_Command_Parameters_For_Quoted_Names()
    {
        var parser = new GetHelpParser();

        var result = parser.Parse("NoSuch'Command; throw 'unexpected", timeoutSeconds: 1);

        Assert.Null(result);
    }
}
