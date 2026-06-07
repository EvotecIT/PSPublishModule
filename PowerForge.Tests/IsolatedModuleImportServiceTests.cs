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
            WriteProfileFiles(moduleBase, ModuleIsolationProfile.MicrosoftTeams.RequiredFiles);
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
            WriteProfileFiles(moduleBase, ModuleIsolationProfile.MicrosoftTeams.RequiredFiles);
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
    public void Prepare_GraphProfile_RemovesNestedModulesAndSkipsDefaultBinaryImport()
    {
        var root = CreateTempDirectory();
        try
        {
            var moduleBase = Path.Combine(root, "Microsoft.Graph.Authentication", "2.37.0");
            Directory.CreateDirectory(Path.Combine(moduleBase, "Dependencies", "Core"));
            Directory.CreateDirectory(Path.Combine(moduleBase, "Dependencies"));
            File.WriteAllText(Path.Combine(moduleBase, "Microsoft.Graph.Authentication.psm1"), CreateGraphLikeScript());
            File.WriteAllText(Path.Combine(moduleBase, "Microsoft.Graph.Authentication.psd1"), CreateGraphLikeManifest());
            WriteProfileFiles(moduleBase, ModuleIsolationProfile.MicrosoftGraphAuthentication.BinaryImports);
            WriteProfileFiles(moduleBase, ModuleIsolationProfile.MicrosoftGraphAuthentication.DependencyAssemblyImports);

            var service = new IsolatedModuleImportService();
            var plan = service.Prepare(new IsolatedModuleImportRequest
            {
                ProfileName = "MicrosoftGraphAuthentication",
                Path = moduleBase,
                WorkRoot = Path.Combine(root, "work")
            });

            Assert.Equal(plan.IsolatedManifestPath, plan.IsolatedImportPath);

            var patched = File.ReadAllText(plan.IsolatedScriptPath);
            Assert.Contains("$ModulePath = (Join-Path $PSScriptRoot 'Microsoft.Graph.Authentication.dll')", patched, StringComparison.Ordinal);
            Assert.Contains("Export-ModuleMember -Cmdlet @('Add-MgEnvironment'", patched, StringComparison.Ordinal);
            Assert.Contains("'Invoke-MgRestMethod'", patched, StringComparison.Ordinal);
            Assert.Contains("'Dependencies/Core/Azure.Core.dll'", patched, StringComparison.Ordinal);
            Assert.DoesNotContain("Export-ModuleMember -Cmdlet (Get-ModuleCmdlet -ModulePath $ModulePath)", patched, StringComparison.Ordinal);
            Assert.DoesNotContain("$null = Import-Module -Name $ModulePath", patched, StringComparison.Ordinal);
            Assert.DoesNotContain("# SIG # Begin signature block", patched, StringComparison.Ordinal);

            var manifest = File.ReadAllText(plan.IsolatedManifestPath);
            Assert.Contains("RootModule = './Microsoft.Graph.Authentication.ALC.psm1'", manifest, StringComparison.Ordinal);
            Assert.Contains("NestedModules = @()", manifest, StringComparison.Ordinal);
            Assert.DoesNotContain("Microsoft.Graph.Authentication.Core.dll')", manifest, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Validate_CompleteExchangeProfile_ReturnsValidResult()
    {
        var root = CreateTempDirectory();
        try
        {
            var moduleBase = Path.Combine(root, "ExchangeOnlineManagement", "3.9.2");
            var netCore = Path.Combine(moduleBase, "netCore");
            Directory.CreateDirectory(netCore);
            File.WriteAllText(Path.Combine(netCore, "ExchangeOnlineManagement.psm1"), CreateExchangeLikeScript());
            File.WriteAllText(Path.Combine(moduleBase, "ExchangeOnlineManagement.psd1"), CreateExchangeLikeManifest("3.9.2"));
            WriteProfileFiles(moduleBase, ModuleIsolationProfile.ExchangeOnlineManagement.BinaryImports.Select(path => Path.Combine("netCore", path)));

            var result = new IsolatedModuleImportService().Validate(new IsolatedModuleImportRequest
            {
                ProfileName = "ExchangeOnlineManagement",
                Path = moduleBase
            });

            Assert.True(result.IsValid);
            Assert.Empty(result.Issues);
            Assert.Equal("ExchangeOnlineManagement", result.ProfileName);
            Assert.Equal(moduleBase, result.SourceModuleBase);
            Assert.Equal(new Version(3, 9, 2), result.ResolvedVersion);
            Assert.Contains(result.Paths, path => path.Category == "SourceScript" && path.Exists);
            Assert.Contains(result.Paths, path => path.Category == "BinaryModule" && path.RelativePath == "Microsoft.Exchange.Management.RestApiClient.dll" && path.Exists);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Validate_MissingProfileBinary_ReturnsIssueAndPrepareFailsBeforeCopy()
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
            var workRoot = Path.Combine(root, "work");

            var service = new IsolatedModuleImportService();
            var result = service.Validate(new IsolatedModuleImportRequest
            {
                ProfileName = "ExchangeOnlineManagement",
                Path = moduleBase
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, issue =>
                issue.Category == "BinaryModule" &&
                issue.Message.Contains("Microsoft.Exchange.Management.ExoPowershellGalleryModule.dll", StringComparison.Ordinal));

            var ex = Assert.Throws<InvalidOperationException>(() => service.Prepare(new IsolatedModuleImportRequest
            {
                ProfileName = "ExchangeOnlineManagement",
                Path = moduleBase,
                WorkRoot = workRoot
            }));

            Assert.Contains("failed preflight validation", ex.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(workRoot));
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

    [Fact]
    public void PrependModuleResolutionPath_AddsPathOnceAtFront()
    {
        var original = Environment.GetEnvironmentVariable("PSModulePath", EnvironmentVariableTarget.Process);
        var root = CreateTempDirectory();
        try
        {
            var first = Path.Combine(root, "first");
            var second = Path.Combine(root, "second");
            var isolated = Path.Combine(root, "isolated");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            Directory.CreateDirectory(isolated);
            Environment.SetEnvironmentVariable("PSModulePath", string.Join(Path.PathSeparator.ToString(), first, second), EnvironmentVariableTarget.Process);

            var normalized = IsolatedModuleImportService.PrependModuleResolutionPath(isolated);
            IsolatedModuleImportService.PrependModuleResolutionPath(isolated);

            var entries = (Environment.GetEnvironmentVariable("PSModulePath", EnvironmentVariableTarget.Process) ?? string.Empty)
                .Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal(Path.GetFullPath(isolated), normalized);
            Assert.Equal(Path.GetFullPath(isolated), entries[0]);
            Assert.Single(entries, entry => string.Equals(Path.GetFullPath(entry), Path.GetFullPath(isolated), PlatformPathComparison));
            Assert.Contains(entries, entry => string.Equals(Path.GetFullPath(entry), Path.GetFullPath(first), PlatformPathComparison));
            Assert.Contains(entries, entry => string.Equals(Path.GetFullPath(entry), Path.GetFullPath(second), PlatformPathComparison));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSModulePath", original, EnvironmentVariableTarget.Process);
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

    private static string CreateGraphLikeScript()
        => string.Join(Environment.NewLine, new[]
        {
            "$ModulePath = (Join-Path $PSScriptRoot 'Microsoft.Graph.Authentication.dll')",
            "$null = Import-Module -Name $ModulePath",
            "Export-ModuleMember",
            "Export-ModuleMember -Cmdlet (Get-ModuleCmdlet -ModulePath $ModulePath) -Alias (Get-ModuleCmdlet -ModulePath $ModulePath -AsAlias)",
            "# SIG # Begin signature block",
            "# signed payload",
            "# SIG # End signature block"
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

    private static string CreateGraphLikeManifest()
        => string.Join(Environment.NewLine, new[]
        {
            "@{",
            "RootModule = './Microsoft.Graph.Authentication.psm1'",
            "ModuleVersion = '2.37.0'",
            "GUID = '883916f2-9184-46ee-b1f8-b6a2fb784cee'",
            "NestedModules = @('Microsoft.Graph.Authentication.dll',",
            "               'Microsoft.Graph.Authentication.Core.dll')",
            "FunctionsToExport = @('Find-MgGraphCommand', 'Find-MgGraphPermission')",
            "CmdletsToExport = @('Connect-MgGraph', 'Disconnect-MgGraph', 'Get-MgContext')",
            "AliasesToExport = @('Connect-Graph')",
            "}"
        });

    private static void WriteProfileFiles(string moduleBase, IEnumerable<string> relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            var path = Path.Combine(
                new[] { moduleBase }.Concat(relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)).ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? moduleBase);
            File.WriteAllText(path, "not a real dll");
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static StringComparison PlatformPathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
