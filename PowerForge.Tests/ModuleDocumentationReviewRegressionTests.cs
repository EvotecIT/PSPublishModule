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
