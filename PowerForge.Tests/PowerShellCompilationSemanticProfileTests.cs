using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellCompilationSemanticProfileTests
{
    [Fact]
    public void TargetIdentityChangesWithSemanticProfile()
    {
        var desktop = CreateTarget(PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId);
        var core = CreateTarget(PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId);

        Assert.Equal(3, desktop.SchemaVersion);
        Assert.Equal(PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId, desktop.SemanticProfileId);
        Assert.Equal(PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, core.SemanticProfileId);
        Assert.NotEqual(desktop.ContractSha256, core.ContractSha256);
    }

    [Fact]
    public void BinderAndBackendEmitProfileOwnedEditionSemantics()
    {
        var document = PowerShellSourceParser.Parse(
            "function Get-Edition { $PSEdition }",
            "profile.ps1");

        var desktop = new PowerShellSemanticCompilationPipeline(
                PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId)
            .Compile(new[] { document }, "net10.0", PowerShellCompilationCapabilities.StaticRuntimeFacts);
        var core = new PowerShellSemanticCompilationPipeline(
                PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId)
            .Compile(new[] { document }, "net10.0", PowerShellCompilationCapabilities.StaticRuntimeFacts);

        Assert.Contains("return \"Desktop\";", Assert.Single(desktop.Emitted.Methods).Source, StringComparison.Ordinal);
        Assert.Contains("return \"Core\";", Assert.Single(core.Emitted.Methods).Source, StringComparison.Ordinal);
    }

    [Fact]
    public void BinderRejectsCoreOnlyAutomaticVariableForDesktopProfile()
    {
        var document = PowerShellSourceParser.Parse(
            "function Test-Platform { $IsWindows }",
            "profile-platform.ps1");

        var desktop = new PowerShellSemanticCompilationPipeline(
                PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId)
            .Compile(new[] { document }, "net10.0", PowerShellCompilationCapabilities.StaticRuntimeFacts);
        var core = new PowerShellSemanticCompilationPipeline(
                PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId)
            .Compile(new[] { document }, "net10.0", PowerShellCompilationCapabilities.StaticRuntimeFacts);

        Assert.Empty(desktop.Emitted.Methods);
        Assert.Contains(desktop.Lowered.Diagnostics, diagnostic => diagnostic.Message.Contains("IsWindows", StringComparison.OrdinalIgnoreCase));
        Assert.Single(core.Emitted.Methods);
    }

    private static PowerShellCompilationTargetContract CreateTarget(string semanticProfileId)
        => PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            "net10.0",
            runtimeIdentifier: null,
            selfContained: false,
            singleFile: false,
            PowerShellCompilationExecutableOptimization.None,
            explicitContract: true,
            semanticProfileId: semanticProfileId);
}
