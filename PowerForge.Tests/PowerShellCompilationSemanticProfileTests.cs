using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellCompilationSemanticProfileTests
{
    [Theory]
    [InlineData("net472", PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId)]
    [InlineData("net8.0", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId)]
    [InlineData("net10.0", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId)]
    public void CompatibilityTargetSelectsFrameworkOwnedSemanticProfile(string targetFramework, string expectedProfileId)
    {
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            targetFramework,
            runtimeIdentifier: null,
            selfContained: false,
            singleFile: false,
            PowerShellCompilationExecutableOptimization.None,
            explicitContract: false);

        Assert.Equal(expectedProfileId, target.SemanticProfileId);
    }

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

    [Theory]
    [InlineData("#requires -Version 5.1\nfunction Get-Value { 42 }", PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId, true)]
    [InlineData("#requires -Version 7.6\nfunction Get-Value { 42 }", PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId, false)]
    [InlineData("#requires -Version 7.6\nfunction Get-Value { 42 }", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, true)]
    [InlineData("#requires -PSEdition Desktop\nfunction Get-Value { 42 }", PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId, true)]
    [InlineData("#requires -PSEdition Desktop\nfunction Get-Value { 42 }", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, false)]
    [InlineData("#requires -PSEdition Core\nfunction Get-Value { 42 }", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, true)]
    [InlineData("#requires -Modules Example.Module\nfunction Get-Value { 42 }", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, false)]
    [InlineData("#requires -RunAsAdministrator\nfunction Get-Value { 42 }", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, false)]
    [InlineData("#requires -PSSnapin Microsoft.PowerShell.Core -Version 3.0\nfunction Get-Value { 42 }", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, false)]
    public void SourceRequirementsAreEvaluatedAgainstTheSelectedProfile(string source, string profileId, bool accepted)
    {
        var document = PowerShellSourceParser.Parse(source, "requirements.ps1");

        var diagnostics = PowerShellSourceSemanticValidator.Validate(document, profileId);

        Assert.Equal(accepted, diagnostics.All(diagnostic => diagnostic.Code != PowerShellCompilationFeatureIds.RequiresDirective));
    }

    [Theory]
    [InlineData("#requires -Version 5.1\nfunction Get-Value { 42 }", PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId)]
    [InlineData("#requires -PSEdition Core\nfunction Get-Value { 42 }", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId)]
    public void AnalyzerCompilesRequirementsSatisfiedByTheSelectedProfile(string source, string profileId)
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForgeRequiresProfileTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sourcePath = Path.Combine(root, "requirements.ps1");
            File.WriteAllText(sourcePath, source);
            var analyzer = new PowerShellCompilationAnalyzer(
                Array.Empty<PowerShellCompilationCommandProviderContract>(),
                profileId);

            var plan = analyzer.Analyze(new PowerShellCompilationSpec(sourcePath, PowerShellCompilationMode.Strict));

            var unit = Assert.Single(Assert.Single(plan.Files).Units);
            Assert.True(unit.IsCompilable, string.Join(Environment.NewLine, unit.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
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
