using System;
using System.IO;

namespace PowerForge.Tests;

public sealed class IsolatedModuleImportServiceTests
{
    [Fact]
    public void Prepare_CopiesModuleAndWritesPatchedScript()
    {
        var root = CreateTempDirectory();
        try
        {
            var moduleBase = Path.Combine(root, "ExchangeOnlineManagement", "3.9.2");
            var netCore = Path.Combine(moduleBase, "netCore");
            Directory.CreateDirectory(netCore);
            File.WriteAllText(Path.Combine(netCore, "ExchangeOnlineManagement.psm1"), CreateExchangeLikeScript());
            File.WriteAllText(Path.Combine(moduleBase, "ExchangeOnlineManagement.psd1"), CreateExchangeLikeManifest("3.9.2"));
            File.WriteAllText(Path.Combine(netCore, "Microsoft.Exchange.Management.RestApiClient.dll"), "not a real dll");
            File.WriteAllText(Path.Combine(netCore, "Microsoft.Exchange.Management.ExoPowershellGalleryModule.dll"), "not a real dll");

            var workRoot = Path.Combine(root, "work");
            var service = new IsolatedModuleImportService();
            var plan = service.Prepare(new IsolatedModuleImportRequest
            {
                ProfileName = "ExchangeOnlineManagement",
                Path = moduleBase,
                WorkRoot = workRoot
            });

            Assert.Equal(moduleBase, plan.SourceModuleBase);
            Assert.StartsWith(workRoot, plan.WorkPath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(plan.IsolatedScriptPath));
            Assert.True(File.Exists(plan.IsolatedManifestPath));
            Assert.Equal(plan.IsolatedManifestPath, plan.IsolatedImportPath);

            var patched = File.ReadAllText(plan.IsolatedScriptPath);
            Assert.Contains("PowerForge.ModuleIsolation.ModuleLoadContext", patched, StringComparison.Ordinal);
            Assert.Contains("function Connect-ExchangeOnline", patched, StringComparison.Ordinal);
            Assert.DoesNotContain("Import-Module $RestModulePath", patched, StringComparison.Ordinal);

            var manifest = File.ReadAllText(plan.IsolatedManifestPath);
            Assert.Contains("RootModule = './netCore/ExchangeOnlineManagement.ALC.psm1'", manifest, StringComparison.Ordinal);
            Assert.Contains("ModuleVersion = '3.9.2'", manifest, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Prepare_TeamsProfile_PreservesManifestExportsAndPatchesSubmoduleBinaryImports()
    {
        var root = CreateTempDirectory();
        try
        {
            var moduleBase = Path.Combine(root, "MicrosoftTeams", "7.8.0");
            var netCore = Path.Combine(moduleBase, "netcoreapp3.1");
            var bin = Path.Combine(moduleBase, "bin");
            Directory.CreateDirectory(netCore);
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(moduleBase, "MicrosoftTeams.psm1"), CreateTeamsLikeScript());
            File.WriteAllText(Path.Combine(moduleBase, "MicrosoftTeams.psd1"), CreateTeamsLikeManifest());
            File.WriteAllText(Path.Combine(moduleBase, "Microsoft.Teams.PowerShell.TeamsCmdlets.psm1"), "$null = Import-Module -Name (Join-Path $PSScriptRoot './netcoreapp3.1/Microsoft.Teams.PowerShell.TeamsCmdlets.dll')");
            File.WriteAllText(Path.Combine(moduleBase, "Microsoft.Teams.Policy.Administration.Cmdlets.Core.psm1"), "$null = Import-Module -Name (Join-Path $PSScriptRoot 'netcoreapp3.1\\Microsoft.Teams.Policy.Administration.Cmdlets.Core.dll')");
            File.WriteAllText(Path.Combine(moduleBase, "Microsoft.Teams.ConfigAPI.Cmdlets.psm1"), "$null = Import-Module -Name (Join-Path $PSScriptRoot './bin/Microsoft.Teams.ConfigAPI.Cmdlets.private.dll')");
            File.WriteAllText(Path.Combine(netCore, "Microsoft.TeamsCmdlets.PowerShell.Connect.dll"), "not a real dll");
            File.WriteAllText(Path.Combine(netCore, "Microsoft.Teams.PowerShell.TeamsCmdlets.dll"), "not a real dll");
            File.WriteAllText(Path.Combine(netCore, "Microsoft.Teams.PowerShell.Module.dll"), "not a real dll");
            File.WriteAllText(Path.Combine(netCore, "Microsoft.Teams.Policy.Administration.Cmdlets.Core.dll"), "not a real dll");
            File.WriteAllText(Path.Combine(netCore, "Microsoft.Teams.Policy.Administration.Cmdlets.Providers.PolicyRp.dll"), "not a real dll");
            File.WriteAllText(Path.Combine(bin, "Microsoft.Teams.ConfigAPI.Cmdlets.private.dll"), "not a real dll");

            var workRoot = Path.Combine(root, "work");
            var service = new IsolatedModuleImportService();
            var plan = service.Prepare(new IsolatedModuleImportRequest
            {
                ProfileName = "MicrosoftTeams",
                Path = moduleBase,
                WorkRoot = workRoot
            });

            Assert.Equal(moduleBase, plan.SourceModuleBase);
            Assert.StartsWith(workRoot, plan.WorkPath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(plan.IsolatedScriptPath));
            Assert.True(File.Exists(plan.IsolatedManifestPath));
            Assert.Equal(plan.IsolatedManifestPath, plan.IsolatedImportPath);

            var patched = File.ReadAllText(plan.IsolatedScriptPath);
            Assert.Contains("PowerForge.ModuleIsolation.ModuleLoadContext", patched, StringComparison.Ordinal);
            Assert.Contains("Microsoft.Teams.PowerShell.TeamsCmdlets.dll", patched, StringComparison.Ordinal);
            Assert.Contains("Microsoft.Teams.ConfigAPI.Cmdlets.private.dll", patched, StringComparison.Ordinal);
            Assert.Contains("Microsoft.Teams.Policy.Administration.psd1", patched, StringComparison.Ordinal);
            Assert.DoesNotContain("function Invoke-OriginalTeamsBootstrap", patched, StringComparison.Ordinal);
            Assert.DoesNotContain("Import-Module './netcoreapp3.1/Microsoft.TeamsCmdlets.PowerShell.Connect.dll'", patched, StringComparison.Ordinal);

            var teamsCmdletsScript = File.ReadAllText(Path.Combine(plan.IsolatedModuleBase, "Microsoft.Teams.PowerShell.TeamsCmdlets.psm1"));
            Assert.Contains("ModuleLoadContext]::LoadModule([System.IO.Path]::GetFullPath", teamsCmdletsScript, StringComparison.Ordinal);
            Assert.Contains("netcoreapp3.1/Microsoft.Teams.PowerShell.TeamsCmdlets.dll", teamsCmdletsScript, StringComparison.Ordinal);
            Assert.DoesNotContain("Import-Module -Name (Join-Path $PSScriptRoot './netcoreapp3.1/Microsoft.Teams.PowerShell.TeamsCmdlets.dll')", teamsCmdletsScript, StringComparison.Ordinal);

            var policyCoreScript = File.ReadAllText(Path.Combine(plan.IsolatedModuleBase, "Microsoft.Teams.Policy.Administration.Cmdlets.Core.psm1"));
            Assert.Contains("ModuleLoadContext]::LoadModule([System.IO.Path]::GetFullPath", policyCoreScript, StringComparison.Ordinal);
            Assert.Contains("netcoreapp3.1/Microsoft.Teams.Policy.Administration.Cmdlets.Core.dll", policyCoreScript, StringComparison.Ordinal);
            Assert.DoesNotContain("Import-Module -Name (Join-Path $PSScriptRoot 'netcoreapp3.1\\Microsoft.Teams.Policy.Administration.Cmdlets.Core.dll')", policyCoreScript, StringComparison.Ordinal);

            var configApiScript = File.ReadAllText(Path.Combine(plan.IsolatedModuleBase, "Microsoft.Teams.ConfigAPI.Cmdlets.psm1"));
            Assert.Contains("Import-Module -Name (Join-Path $PSScriptRoot './bin/Microsoft.Teams.ConfigAPI.Cmdlets.private.dll')", configApiScript, StringComparison.Ordinal);

            var manifest = File.ReadAllText(plan.IsolatedManifestPath);
            Assert.Contains("RootModule = './MicrosoftTeams.ALC.psm1'", manifest, StringComparison.Ordinal);
            Assert.Contains("FunctionsToExport = @('Get-Team')", manifest, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Prepare_ExplicitManifestPathWithDifferentName_UsesResolvedManifest()
    {
        var root = CreateTempDirectory();
        try
        {
            var moduleBase = Path.Combine(root, "ContosoTeams", "7.9.0");
            var netCore = Path.Combine(moduleBase, "netcoreapp3.1");
            var bin = Path.Combine(moduleBase, "bin");
            Directory.CreateDirectory(netCore);
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(moduleBase, "MicrosoftTeams.psm1"), CreateTeamsLikeScript());
            File.WriteAllText(Path.Combine(moduleBase, "ContosoTeams.psd1"), CreateTeamsLikeManifest("7.9.0", "Get-ContosoTeam"));
            File.WriteAllText(Path.Combine(netCore, "Microsoft.TeamsCmdlets.PowerShell.Connect.dll"), "not a real dll");
            File.WriteAllText(Path.Combine(netCore, "Microsoft.Teams.PowerShell.TeamsCmdlets.dll"), "not a real dll");
            File.WriteAllText(Path.Combine(netCore, "Microsoft.Teams.PowerShell.Module.dll"), "not a real dll");
            File.WriteAllText(Path.Combine(netCore, "Microsoft.Teams.Policy.Administration.Cmdlets.Core.dll"), "not a real dll");
            File.WriteAllText(Path.Combine(netCore, "Microsoft.Teams.Policy.Administration.Cmdlets.Providers.PolicyRp.dll"), "not a real dll");
            File.WriteAllText(Path.Combine(bin, "Microsoft.Teams.ConfigAPI.Cmdlets.private.dll"), "not a real dll");

            var service = new IsolatedModuleImportService();
            var plan = service.Prepare(new IsolatedModuleImportRequest
            {
                ProfileName = "MicrosoftTeams",
                Path = Path.Combine(moduleBase, "ContosoTeams.psd1"),
                WorkRoot = Path.Combine(root, "work")
            });

            var manifest = File.ReadAllText(plan.IsolatedManifestPath);
            Assert.Contains("RootModule = './MicrosoftTeams.ALC.psm1'", manifest, StringComparison.Ordinal);
            Assert.Contains("ModuleVersion = '7.9.0'", manifest, StringComparison.Ordinal);
            Assert.Contains("FunctionsToExport = @('Get-ContosoTeam')", manifest, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Prepare_ExplicitPathBelowMinimumVersion_FailsBeforePatching()
    {
        var root = CreateTempDirectory();
        try
        {
            var moduleBase = Path.Combine(root, "MicrosoftTeams", "7.1.0");
            Directory.CreateDirectory(moduleBase);
            File.WriteAllText(Path.Combine(moduleBase, "MicrosoftTeams.psm1"), CreateTeamsLikeScript());
            File.WriteAllText(Path.Combine(moduleBase, "MicrosoftTeams.psd1"), CreateTeamsLikeManifest("7.1.0"));

            var service = new IsolatedModuleImportService();
            var ex = Assert.Throws<InvalidOperationException>(() => service.Prepare(new IsolatedModuleImportRequest
            {
                ProfileName = "MicrosoftTeams",
                Path = moduleBase,
                WorkRoot = Path.Combine(root, "work")
            }));

            Assert.Contains("requires MicrosoftTeams 7.8.0", ex.Message, StringComparison.Ordinal);
            Assert.Contains("Resolved version 7.1.0", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateExchangeLikeScript()
        => string.Join(Environment.NewLine, new[]
        {
            "# Import the REST module so that the EXO* cmdlets are present before Connect-ExchangeOnline in the powershell instance.",
            "$RestModule = \"Microsoft.Exchange.Management.RestApiClient.dll\"",
            "$RestModulePath = [System.IO.Path]::Combine($PSScriptRoot, $RestModule)",
            "Import-Module $RestModulePath",
            "",
            "$ExoPowershellModule = \"Microsoft.Exchange.Management.ExoPowershellGalleryModule.dll\"",
            "$ExoPowershellModulePath = [System.IO.Path]::Combine($PSScriptRoot, $ExoPowershellModule)",
            "Import-Module $ExoPowershellModulePath",
            "function Connect-ExchangeOnline { 'body' }"
        });

    private static string CreateTeamsLikeScript()
        => string.Join(Environment.NewLine, new[]
        {
            "Import-Module './netcoreapp3.1/Microsoft.TeamsCmdlets.PowerShell.Connect.dll'",
            "function Invoke-OriginalTeamsBootstrap { 'body' }"
        });

    private static string CreateExchangeLikeManifest(string version)
        => string.Join(Environment.NewLine, new[]
        {
            "@{",
            "RootModule = if($PSEdition -eq 'Core')",
            "{",
            "    './netCore/ExchangeOnlineManagement.psm1'",
            "}",
            "else",
            "{",
            "    './netFramework/ExchangeOnlineManagement.psm1'",
            "}",
            "ModuleVersion = '" + version + "'",
            "GUID = '2927a85d-904c-4bf6-b35f-5c93682f5656'",
            "FunctionsToExport = @('Connect-ExchangeOnline')",
            "CmdletsToExport = @()",
            "}"
        });

    private static string CreateTeamsLikeManifest(string version = "7.8.0", string functionName = "Get-Team")
        => string.Join(Environment.NewLine, new[]
        {
            "@{",
            "RootModule = './MicrosoftTeams.psm1'",
            "ModuleVersion = '" + version + "'",
            "GUID = 'd910df43-3ca6-4c9c-a2e3-e9f45a8e2ad9'",
            "FunctionsToExport = @('" + functionName + "')",
            "CmdletsToExport = @('Connect-MicrosoftTeams')",
            "}"
        });

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
