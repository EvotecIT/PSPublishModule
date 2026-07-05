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
    public void Read_SkipsNonModuleRequiresBeforeScriptHelp()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Invoke-Company.ps1");
        File.WriteAllText(path, """
<#PSScriptInfo

.VERSION 1.0.0.0

.GUID 11111111-2222-3333-4444-555555555555

#>

#Requires -Version 6.0
#Requires -RunAsAdministrator

<#
.DESCRIPTION
Script description after requirements.
#>

"ok"
""");

        var result = new ManagedScriptFileInfoService().Read(path);

        Assert.Equal("Script description after requirements.", result.Description);
        Assert.Contains("#Requires -Version 6.0", result.ScriptHelp, StringComparison.Ordinal);
        Assert.DoesNotContain("#Requires -Version 6.0", result.ScriptContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_SplitsCommaSeparatedListMetadataValues()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Invoke-Company.ps1");
        File.WriteAllText(path, """
<#PSScriptInfo

.VERSION 1.0.0.0

.GUID 11111111-2222-3333-4444-555555555555

.AUTHOR Evotec

.EXTERNALMODULEDEPENDENCIES Microsoft.Graph.Users, Microsoft.Graph.Identity.Governance

.REQUIREDSCRIPTS Initialize-Company,Invoke-Other

#>

<#
.DESCRIPTION
Demo script
#>
""");

        var result = new ManagedScriptFileInfoService().Read(path);

        Assert.Equal(new[] { "Microsoft.Graph.Users", "Microsoft.Graph.Identity.Governance" }, result.ExternalModuleDependencies);
        Assert.Equal(new[] { "Initialize-Company", "Invoke-Other" }, result.RequiredScripts);
    }

    [Fact]
    public void Read_PreservesDottedLinesInsideScriptDescription()
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
First line.
.NET support added.
Final line.

.EXAMPLE
Invoke-Company
#>
""");

        var result = new ManagedScriptFileInfoService().Read(path);

        Assert.Contains(".NET support added.", result.Description, StringComparison.Ordinal);
        Assert.Contains("Final line.", result.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(".EXAMPLE", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_ParsesSingleLineScriptHelpComments()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Invoke-Company.ps1");
        File.WriteAllText(path, """
<#PSScriptInfo

.VERSION 1.0.0.0

.GUID 11111111-2222-3333-4444-555555555555

#>

# .DESCRIPTION
# Script description from line comments.
#
# .EXAMPLE
# Invoke-Company

function Invoke-Company { "ok" }
""");

        var result = new ManagedScriptFileInfoService().Read(path);

        Assert.Equal("Script description from line comments.", result.Description);
        Assert.DoesNotContain(".EXAMPLE", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_SkipsLeadingHeaderCommentsBeforeScriptHelp()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Invoke-Company.ps1");
        File.WriteAllText(path, """
<#PSScriptInfo

.VERSION 1.0.0.0

.GUID 11111111-2222-3333-4444-555555555555

#>

# Copyright Contoso
# Generated by build

<#
.DESCRIPTION
Script description after header comments.
#>

function Invoke-Company { "ok" }
""");

        var result = new ManagedScriptFileInfoService().Read(path);

        Assert.Equal("Script description after header comments.", result.Description);
        Assert.Contains("# Copyright Contoso", result.ScriptContent, StringComparison.Ordinal);
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
    public void Read_SplitsCommaSeparatedRequiresModuleNames()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Invoke-Company.ps1");
        File.WriteAllText(path, """
<#PSScriptInfo

.VERSION 1.0.0.0

.GUID 11111111-2222-3333-4444-555555555555

#>

#Requires -Modules AzureRM.Netcore, PowerShellGet
""");

        var modules = new ManagedScriptFileInfoService().Read(path).RequiredModules;

        Assert.Equal(new[] { "AzureRM.Netcore", "PowerShellGet" }, modules.Select(static module => module.ModuleName));
    }

    [Fact]
    public void Read_IgnoresCommentedRequiresExamples()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Invoke-Company.ps1");
        File.WriteAllText(path, """
<#PSScriptInfo

.VERSION 1.0.0.0

.GUID 11111111-2222-3333-4444-555555555555

.RELEASENOTES
Example:
#Requires -Module Pester

#>

<#
.DESCRIPTION
Demo script.

.EXAMPLE
#Requires -Module Az.Accounts
#>
""");

        var result = new ManagedScriptFileInfoService().Read(path);

        Assert.Empty(result.RequiredModules);
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
    public void Test_ReturnsFalseForIncompleteMetadata()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Invoke-Company.ps1");
        File.WriteAllText(path, """
<#PSScriptInfo

.VERSION 1.0.0

.GUID 11111111-2222-3333-4444-555555555555

#>
""");

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
    public void Update_PreservesExistingGuidWhenSparseUpdateOmitsGuid()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Invoke-Company.ps1");
        var service = new ManagedScriptFileInfoService();
        var guid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        service.Create(new ManagedScriptFileInfo
        {
            Path = path,
            Guid = guid,
            Author = "Evotec",
            Description = "Before description"
        }, overwrite: false);

        var result = service.Update(path, new ManagedScriptFileInfo
        {
            Version = "1.2.3"
        }, removeSignature: false);

        Assert.Equal(guid, result.Guid);
    }

    [Fact]
    public void Update_PreservesFunctionHelpWhenScriptHelpIsAbsent()
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

        new ManagedScriptFileInfoService().Update(path, new ManagedScriptFileInfo { Version = "1.2.3" }, removeSignature: false);
        var text = File.ReadAllText(path);

        Assert.Contains("Function description.", text, StringComparison.Ordinal);
        Assert.Contains("function Invoke-Company", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_PreservesExistingScriptHelpSections()
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

.PARAMETER Name
Company name.

.EXAMPLE
Invoke-Company -Name Evotec
#>

param([string] $Name)
""");

        new ManagedScriptFileInfoService().Update(path, new ManagedScriptFileInfo { Version = "1.2.3" }, removeSignature: false);
        var text = File.ReadAllText(path);

        Assert.Contains(".PARAMETER Name", text, StringComparison.Ordinal);
        Assert.Contains("Company name.", text, StringComparison.Ordinal);
        Assert.Contains(".EXAMPLE", text, StringComparison.Ordinal);
        Assert.Contains("Invoke-Company -Name Evotec", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_AddsDescriptionToExistingScriptHelpWithoutDuplicatingHelp()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Invoke-Company.ps1");
        File.WriteAllText(path, """
<#PSScriptInfo

.VERSION 1.0.0.0

.GUID 11111111-2222-3333-4444-555555555555

#>

<#
.SYNOPSIS
Runs the workflow.

.EXAMPLE
Invoke-Company
#>

function Invoke-Company { "ok" }
""");

        new ManagedScriptFileInfoService().Update(path, new ManagedScriptFileInfo { Description = "Script description." }, removeSignature: false);
        var text = File.ReadAllText(path);

        Assert.Contains(".DESCRIPTION", text, StringComparison.Ordinal);
        Assert.Contains("Script description.", text, StringComparison.Ordinal);
        Assert.Contains(".SYNOPSIS", text, StringComparison.Ordinal);
        Assert.Contains("Runs the workflow.", text, StringComparison.Ordinal);
        Assert.Single(IndexesOf(text, ".SYNOPSIS"));
        Assert.True(text.IndexOf(".DESCRIPTION", StringComparison.Ordinal) < text.IndexOf(".SYNOPSIS", StringComparison.Ordinal));
    }

    [Fact]
    public void Update_PreservesSingleLineScriptHelpComments()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Invoke-Company.ps1");
        File.WriteAllText(path, """
<#PSScriptInfo

.VERSION 1.0.0.0

.GUID 11111111-2222-3333-4444-555555555555

#>

# .DESCRIPTION
# Script description.
#
# .EXAMPLE
# Invoke-Company

function Invoke-Company { "ok" }
""");

        new ManagedScriptFileInfoService().Update(path, new ManagedScriptFileInfo
        {
            Version = "1.2.3",
            Description = "Updated line-comment description."
        }, removeSignature: false);
        var text = File.ReadAllText(path);

        Assert.Contains("# .DESCRIPTION", text, StringComparison.Ordinal);
        Assert.Contains("# Updated line-comment description.", text, StringComparison.Ordinal);
        Assert.Contains("# .EXAMPLE", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<#" + Environment.NewLine + Environment.NewLine + ".DESCRIPTION" + Environment.NewLine + Environment.NewLine, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_ReplacesModuleRequiresThatAppearAfterScriptHelp()
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

#Requires -Module Pester

function Invoke-Company { "ok" }
""");

        var result = new ManagedScriptFileInfoService().Update(path, new ManagedScriptFileInfo
        {
            RequiredModules = [new ManagedScriptRequiredModule { ModuleName = "Az.Accounts" }],
            RequiredModulesSpecified = true
        }, removeSignature: false);
        var text = File.ReadAllText(path);

        Assert.Equal("Az.Accounts", Assert.Single(result.RequiredModules).ModuleName);
        Assert.Contains("#Requires -Module Az.Accounts", text, StringComparison.Ordinal);
        Assert.DoesNotContain("#Requires -Module Pester", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_ReplacesModuleRequiresThatAppearInsideFunctionBody()
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
    #Requires -Module Pester
    "ok"
}
""");

        var result = new ManagedScriptFileInfoService().Update(path, new ManagedScriptFileInfo
        {
            RequiredModules = [new ManagedScriptRequiredModule { ModuleName = "Az.Accounts" }],
            RequiredModulesSpecified = true
        }, removeSignature: false);
        var text = File.ReadAllText(path);

        Assert.Equal("Az.Accounts", Assert.Single(result.RequiredModules).ModuleName);
        Assert.Contains("#Requires -Module Az.Accounts", text, StringComparison.Ordinal);
        Assert.DoesNotContain("#Requires -Module Pester", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_KeepsNonModuleRequiresBeforeScriptHelp()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Invoke-Company.ps1");
        File.WriteAllText(path, """
<#PSScriptInfo

.VERSION 1.0.0.0

.GUID 11111111-2222-3333-4444-555555555555

#>

#Requires -Version 6.0
#Requires -PSEdition Core

<#
.DESCRIPTION
Script description.
#>

function Invoke-Company { "ok" }
""");

        new ManagedScriptFileInfoService().Update(path, new ManagedScriptFileInfo { Version = "1.2.3" }, removeSignature: false);
        var text = File.ReadAllText(path);

        Assert.True(text.IndexOf("#Requires -Version 6.0", StringComparison.Ordinal) < text.IndexOf(".DESCRIPTION", StringComparison.Ordinal));
        Assert.True(text.IndexOf("#Requires -PSEdition Core", StringComparison.Ordinal) < text.IndexOf(".DESCRIPTION", StringComparison.Ordinal));
    }

    [Fact]
    public void Update_RefusesSignedScriptUnlessSignatureIsRemoved()
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

"ok"
# SIG # Begin signature block
# signed
# SIG # End signature block
""");

        var service = new ManagedScriptFileInfoService();

        Assert.Throws<InvalidOperationException>(() => service.Update(path, new ManagedScriptFileInfo { Version = "1.2.3" }, removeSignature: false));

        var result = service.Update(path, new ManagedScriptFileInfo { Version = "1.2.3" }, removeSignature: true);
        var text = File.ReadAllText(path);

        Assert.Equal("1.2.3", result.Version);
        Assert.DoesNotContain("# SIG # Begin signature block", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Update_KeepsScriptHelpSeparatedFromFirstFunction()
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


function Invoke-Company { "ok" }
""");

        new ManagedScriptFileInfoService().Update(path, new ManagedScriptFileInfo { Version = "1.2.3" }, removeSignature: false);
        var text = File.ReadAllText(path);

        Assert.Contains("#>" + Environment.NewLine + Environment.NewLine + Environment.NewLine + "function Invoke-Company", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_CanClearListMetadataWhenExplicitlySpecified()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Invoke-Company.ps1");
        var service = new ManagedScriptFileInfoService();
        service.Create(new ManagedScriptFileInfo
        {
            Path = path,
            Tags = new[] { "company", "report" },
            RequiredModules = [new ManagedScriptRequiredModule { ModuleName = "Pester" }]
        }, overwrite: false);

        var result = service.Update(path, new ManagedScriptFileInfo
        {
            Tags = Array.Empty<string>(),
            TagsSpecified = true,
            RequiredModules = Array.Empty<ManagedScriptRequiredModule>(),
            RequiredModulesSpecified = true
        }, removeSignature: false);

        Assert.Empty(result.Tags);
        Assert.Empty(result.RequiredModules);
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

    [Fact]
    public void Render_RejectsConflictingRequiredModuleVersionKeys()
    {
        var service = new ManagedScriptFileInfoService();

        var ex = Assert.Throws<InvalidOperationException>(() => service.Render(new ManagedScriptFileInfo
        {
            Path = "Invoke-Company.ps1",
            Guid = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            RequiredModules =
            [
                new ManagedScriptRequiredModule
                {
                    ModuleName = "Pester",
                    RequiredVersion = "5.7.0",
                    MaximumVersion = "6.0.0"
                }
            ]
        }));

        Assert.Contains("cannot combine RequiredVersion", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_RejectsGuidOnlyRequiredModuleHashtable()
    {
        var service = new ManagedScriptFileInfoService();

        var ex = Assert.Throws<InvalidOperationException>(() => service.Render(new ManagedScriptFileInfo
        {
            Path = "Invoke-Company.ps1",
            Guid = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            RequiredModules =
            [
                new ManagedScriptRequiredModule
                {
                    ModuleName = "Pester",
                    Guid = "11111111-2222-3333-4444-555555555555"
                }
            ]
        }));

        Assert.Contains("with Guid must include", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_RejectsInvalidScriptVersion()
    {
        var service = new ManagedScriptFileInfoService();

        var ex = Assert.Throws<InvalidOperationException>(() => service.Render(new ManagedScriptFileInfo
        {
            Path = "Invoke-Company.ps1",
            Version = "banana",
            Guid = Guid.Parse("11111111-2222-3333-4444-555555555555")
        }));

        Assert.Contains("not a valid version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("1.0.0 +build")]
    [InlineData("1.0.0+ build")]
    [InlineData("1 .0.0")]
    [InlineData("1. 0.0")]
    public void Render_RejectsSeparatorPaddedScriptVersion(string version)
    {
        var service = new ManagedScriptFileInfoService();

        var ex = Assert.Throws<InvalidOperationException>(() => service.Render(new ManagedScriptFileInfo
        {
            Path = "Invoke-Company.ps1",
            Version = version,
            Guid = Guid.Parse("11111111-2222-3333-4444-555555555555")
        }));

        Assert.Contains("not a valid version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<int> IndexesOf(string text, string value)
    {
        var indexes = new List<int>();
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            indexes.Add(index);
            index += value.Length;
        }

        return indexes;
    }
}
