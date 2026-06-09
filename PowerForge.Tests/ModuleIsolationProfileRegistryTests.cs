using System;
using System.Linq;

namespace PowerForge.Tests;

public sealed class ModuleIsolationProfileRegistryTests
{
    [Fact]
    public void Resolve_ReturnsBuiltInExchangeOnlineManagementProfile()
    {
        var profile = new ModuleIsolationProfileRegistry().Resolve("ExchangeOnlineManagement");

        Assert.Equal("ExchangeOnlineManagement", profile.ModuleName);
        Assert.Equal("netCore/ExchangeOnlineManagement.psm1", profile.ScriptRelativePath);
        Assert.Equal("ExchangeOnlineManagement.ALC.psm1", profile.IsolatedScriptName);
        Assert.Equal("ExchangeOnlineManagement.psd1", profile.ManifestRelativePath);
        Assert.Equal("ExchangeOnlineManagement.psd1", profile.IsolatedManifestName);
        Assert.Equal(8, profile.SourceLinesToSkip);
        Assert.Contains("Microsoft.Exchange.Management.RestApiClient.dll", profile.BinaryImports);
        Assert.Contains("Microsoft.Exchange.Management.", profile.TypeAcceleratorNamespaces);
    }

    [Fact]
    public void Resolve_ReturnsBuiltInMicrosoftTeamsProfile()
    {
        var profile = new ModuleIsolationProfileRegistry().Resolve("MicrosoftTeams");

        Assert.Equal("MicrosoftTeams", profile.ModuleName);
        Assert.Equal("MicrosoftTeams.psm1", profile.ScriptRelativePath);
        Assert.Equal("MicrosoftTeams.ALC.psm1", profile.IsolatedScriptName);
        Assert.Equal("MicrosoftTeams.psd1", profile.ManifestRelativePath);
        Assert.Equal("MicrosoftTeams.ALC.psd1", profile.IsolatedManifestName);
        Assert.False(profile.IncludeSourceScriptBody);
        Assert.Contains(profile.AdditionalScriptLines, line => line.Contains("Microsoft.Teams.PowerShell.TeamsCmdlets.psd1", StringComparison.Ordinal));
        Assert.Contains(profile.AdditionalScriptLines, line => line.Contains("Microsoft.Teams.Policy.Administration.psd1", StringComparison.Ordinal));
        Assert.Contains(profile.AdditionalScriptLines, line => line.Contains("Microsoft.Teams.ConfigAPI.Cmdlets.psd1", StringComparison.Ordinal));
        Assert.Contains("netcoreapp3.1/Microsoft.Teams.PowerShell.TeamsCmdlets.dll", profile.BinaryImports);
        Assert.Contains("bin/Microsoft.Teams.ConfigAPI.Cmdlets.private.dll", profile.BinaryImports);
        Assert.Contains("Microsoft.Teams.", profile.TypeAcceleratorNamespaces);
    }

    [Fact]
    public void Resolve_ReturnsBuiltInMicrosoftGraphAuthenticationProfile()
    {
        var profile = new ModuleIsolationProfileRegistry().Resolve("MicrosoftGraphAuthentication");

        Assert.Equal("Microsoft.Graph.Authentication", profile.ModuleName);
        Assert.Equal("Microsoft.Graph.Authentication.psm1", profile.ScriptRelativePath);
        Assert.Equal("Microsoft.Graph.Authentication.ALC.psm1", profile.IsolatedScriptName);
        Assert.Equal("Microsoft.Graph.Authentication.psd1", profile.ManifestRelativePath);
        Assert.Equal("Microsoft.Graph.Authentication.ALC.psd1", profile.IsolatedManifestName);
        Assert.True(profile.RemoveSourceSignatureBlock);
        Assert.True(profile.RemoveManifestNestedModules);
        Assert.Contains("Import-Module -Name $ModulePath", profile.SourceLineContainsToSkip);
        Assert.Contains("Export-ModuleMember -Cmdlet (Get-ModuleCmdlet -ModulePath $ModulePath)", profile.SourceLineContainsToSkip);
        Assert.Contains(profile.AdditionalScriptLines, line => line.Contains("Connect-MgGraph", StringComparison.Ordinal));
        Assert.Contains(profile.AdditionalScriptLines, line => line.Contains("Invoke-MgRestMethod", StringComparison.Ordinal));
        Assert.Contains("Dependencies/Core/Azure.Core.dll", profile.DependencyAssemblyImports);
        Assert.Contains("Dependencies/Microsoft.Kiota.Abstractions.dll", profile.DependencyAssemblyImports);
        Assert.Contains("Microsoft.Graph.Authentication.dll", profile.BinaryImports);
        Assert.Contains("Microsoft.Graph.", profile.TypeAcceleratorNamespaces);
    }

    [Fact]
    public void Resolve_UnknownProfile_ListsAvailableProfiles()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new ModuleIsolationProfileRegistry().Resolve("MissingProfile"));

        Assert.Contains("MissingProfile", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ExchangeOnlineManagement", ex.Message, StringComparison.Ordinal);
        Assert.Contains("MicrosoftTeams", ex.Message, StringComparison.Ordinal);
        Assert.Contains("MicrosoftGraphAuthentication", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_CustomProfile_OverridesBuiltInProfile()
    {
        var custom = new ModuleIsolationProfile
        {
            Name = "ExchangeOnlineManagement",
            ModuleName = "CustomExchange"
        };

        var profile = new ModuleIsolationProfileRegistry(new[] { custom }).Profiles.Single(item => item.Name == "ExchangeOnlineManagement");

        Assert.Equal("CustomExchange", profile.ModuleName);
    }
}
