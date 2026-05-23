using System.Collections;
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
    public void GetHelpParser_Uses_Command_Parameters_For_Quoted_Names()
    {
        var parser = new GetHelpParser();

        var result = parser.Parse("NoSuch'Command; throw 'unexpected", timeoutSeconds: 1);

        Assert.Null(result);
    }
}
