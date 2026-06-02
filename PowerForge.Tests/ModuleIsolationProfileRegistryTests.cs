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
    public void Resolve_UnknownProfile_ListsAvailableProfiles()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new ModuleIsolationProfileRegistry().Resolve("MissingProfile"));

        Assert.Contains("MissingProfile", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ExchangeOnlineManagement", ex.Message, StringComparison.Ordinal);
        Assert.Contains("MicrosoftTeams", ex.Message, StringComparison.Ordinal);
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
