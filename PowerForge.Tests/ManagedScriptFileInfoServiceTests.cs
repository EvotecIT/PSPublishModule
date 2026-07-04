namespace PowerForge.Tests;

public sealed class ManagedScriptFileInfoServiceTests
{
    [Fact]
    public void Create_WritesPSResourceGetCompatibleMetadataAndRequiredModules()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Invoke-Company.ps1");
        var service = new ManagedScriptFileInfoService();

        var result = service.Create(new ManagedScriptFileInfo
        {
            Path = path,
            Version = "2.0.0",
            Guid = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Author = "Evotec",
            Description = "Runs the company workflow.",
            Tags = new[] { "company", "automation" },
            ExternalModuleDependencies = new[] { "NetSecurity" },
            RequiredScripts = new[] { "Initialize-Company" },
            ExternalScriptDependencies = new[] { "Invoke-External" },
            ReleaseNotes = "Initial release",
            PrivateData = "private",
            RequiredModules =
            [
                new ManagedScriptRequiredModule
                {
                    ModuleName = "Microsoft.Graph",
                    RequiredVersion = "2.38.0"
                }
            ]
        }, overwrite: false);

        var text = File.ReadAllText(path);

        Assert.Equal("Invoke-Company", result.Name);
        Assert.Equal("2.0.0", result.Version);
        Assert.Contains("<#PSScriptInfo", text, StringComparison.Ordinal);
        Assert.Contains(".TAGS company automation", text, StringComparison.Ordinal);
        Assert.Contains("#Requires -Module @{ ModuleName = 'Microsoft.Graph'; RequiredVersion = '2.38.0' }", text, StringComparison.Ordinal);
        Assert.Equal("Runs the company workflow.", result.Description);
        Assert.Equal("Microsoft.Graph", Assert.Single(result.RequiredModules).ModuleName);
    }

    [Fact]
    public void Read_ParsesExistingPSResourceGetMetadataShape()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Invoke-Company.ps1");
        File.WriteAllText(path, """
<#PSScriptInfo

.VERSION 1.0.0.0

.GUID 11111111-2222-3333-4444-555555555555

.AUTHOR Evotec

.COMPANYNAME

.COPYRIGHT

.TAGS alpha beta

.LICENSEURI https://example.test/license

.PROJECTURI https://example.test/project

.ICONURI

.EXTERNALMODULEDEPENDENCIES NetSecurity

.REQUIREDSCRIPTS Initialize-Company

.EXTERNALSCRIPTDEPENDENCIES Invoke-External

.RELEASENOTES
Ready

.PRIVATEDATA
data

#>

#Requires -Module @{ ModuleName = 'Az.Accounts'; ModuleVersion = '3.0.0' }

<#

.DESCRIPTION
Demo script

#>

"ok"
""");

        var result = new ManagedScriptFileInfoService().Read(path);

        Assert.Equal("Invoke-Company", result.Name);
        Assert.Equal("1.0.0.0", result.Version);
        Assert.Equal(new[] { "alpha", "beta" }, result.Tags);
        Assert.Equal("https://example.test/project", result.ProjectUri);
        Assert.Equal("Ready", result.ReleaseNotes);
        Assert.Equal("Demo script", result.Description);
        Assert.Equal("Az.Accounts", Assert.Single(result.RequiredModules).ModuleName);
        Assert.Contains("\"ok\"", result.ScriptContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_PrefersScriptLevelDescriptionOverLaterFunctionHelp()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Invoke-Company.ps1");
        File.WriteAllText(path, """
<#PSScriptInfo

.VERSION 1.0.0.0

.GUID 11111111-2222-3333-4444-555555555555

#>

<#
.DESCRIPTION
Script description.
#>

function Invoke-Company {
<#
.DESCRIPTION
Function description.
#>
}
""");

        var result = new ManagedScriptFileInfoService().Read(path);

        Assert.Equal("Script description.", result.Description);
    }

    [Fact]
    public void Read_DoesNotTreatLaterFunctionHelpAsScriptDescription()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Invoke-Company.ps1");
        File.WriteAllText(path, """
<#PSScriptInfo

.VERSION 1.0.0.0

.GUID 11111111-2222-3333-4444-555555555555

#>

function Invoke-Company {
<#
.DESCRIPTION
Function description.
#>
}
""");

        var result = new ManagedScriptFileInfoService().Read(path);

        Assert.Equal(string.Empty, result.Description);
    }

    [Fact]
    public void Read_PreservesDottedLinesInsideMultilineMetadataValues()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Invoke-Company.ps1");
        File.WriteAllText(path, """
<#PSScriptInfo

.VERSION 1.0.0.0

.GUID 11111111-2222-3333-4444-555555555555

.RELEASENOTES
Initial release.
.NET support added.
Another note.

#>

<#
.DESCRIPTION
Demo script
#>
""");

        var result = new ManagedScriptFileInfoService().Read(path);

        Assert.Contains(".NET support added.", result.ReleaseNotes, StringComparison.Ordinal);
        Assert.Contains("Another note.", result.ReleaseNotes, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_ParsesPluralRequiresAndDoubleQuotedModuleHashtableValues()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Invoke-Company.ps1");
        File.WriteAllText(path, """
<#PSScriptInfo

.VERSION 1.0.0.0

.GUID 11111111-2222-3333-4444-555555555555

#>

#Requires -Modules @{ ModuleName = "Pester"; RequiredVersion = "5.7.0" }

<#
.DESCRIPTION
Demo script
#>
""");

        var module = Assert.Single(new ManagedScriptFileInfoService().Read(path).RequiredModules);

        Assert.Equal("Pester", module.ModuleName);
        Assert.Equal("5.7.0", module.RequiredVersion);
    }

    [Fact]
    public void Test_ReturnsFalseForScriptWithoutMetadata()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Invoke-Company.ps1");
        File.WriteAllText(path, "\"ok\"");

        Assert.False(new ManagedScriptFileInfoService().Test(path));
    }

    [Fact]
    public void Update_PreservesScriptBodyAndUpdatesSelectedMetadata()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Invoke-Company.ps1");
        var service = new ManagedScriptFileInfoService();
        service.Create(new ManagedScriptFileInfo
        {
            Path = path,
            Author = "Before",
            Description = "Before description",
            ScriptContent = "\"body\""
        }, overwrite: false);

        var result = service.Update(path, new ManagedScriptFileInfo
        {
            Version = "1.2.3",
            Author = "After",
            Description = "After description",
            Tags = new[] { "updated" }
        }, removeSignature: false);
        var text = File.ReadAllText(path);

        Assert.Equal("1.2.3", result.Version);
        Assert.Equal("After", result.Author);
        Assert.Equal("After description", result.Description);
        Assert.Contains("\"body\"", text, StringComparison.Ordinal);
        Assert.Contains(".TAGS updated", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WritesNameOnlyRequiredModulesAsSimpleStrings()
    {
        var text = new ManagedScriptFileInfoService().Render(new ManagedScriptFileInfo
        {
            Path = "Invoke-Company.ps1",
            Guid = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            RequiredModules = [new ManagedScriptRequiredModule { ModuleName = "Pester" }]
        });

        Assert.Contains("#Requires -Module Pester", text, StringComparison.Ordinal);
        Assert.DoesNotContain("@{ ModuleName = 'Pester' }", text, StringComparison.Ordinal);
    }
}
