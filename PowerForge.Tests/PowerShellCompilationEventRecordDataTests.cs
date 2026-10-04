using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    private const string BorrowedEventRecordFunctions = """
        function Read-BorrowedEvent { param([System.Diagnostics.Eventing.Reader.EventLogRecord]$Record) $Record }
        function Read-BorrowedEvents {
            [CmdletBinding()]param([Parameter(ValueFromPipeline)][System.Diagnostics.Eventing.Reader.EventLogRecord[]]$Records)
            process { foreach($record in $Records){$record} }
        }
        function Read-BorrowedEventList { param([Collections.Generic.List[System.Diagnostics.Eventing.Reader.EventLogRecord]]$Records) foreach($record in $Records){$record} }
        function Read-BorrowedEventMap { param([Collections.Generic.Dictionary[string,System.Diagnostics.Eventing.Reader.EventLogRecord]]$Records) foreach($key in $Records.Keys){$Records[$key]} }
        """;

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void EventRecordData_RequiresNativeBindingAndHostTypes(string framework)
    {
        var source = PowerShellSourceParser.Parse(BorrowedEventRecordFunctions,
            Path.Combine(Path.GetTempPath(), "borrowed-event.psm1"));
        foreach (var capabilities in new[]
        {
            PowerShellCompilationCapabilities.TypedLibrary,
            PowerShellCompilationCapabilities.BinaryModule,
            PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.NativeFunctionBinding,
            PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.PowerShellHostTypes
        })
            Assert.Empty(new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework, capabilities).Emitted.Methods);
        var qualified = new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework,
            PowerShellCompilationCapabilities.HybridModule);
        Assert.Equal(4, qualified.Emitted.Methods.Count());
        Assert.All(qualified.Analyzed.Functions, function => Assert.NotNull(function.NativeFunctionBinding));
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void EventRecordData_CompilesPinnedConversion(string framework)
    {
        var source = FindCompleteConversionWorkflow("PSScriptTools", "EventRecord", "Convert-EventLogRecord.ps1");
        Assert.Equal("0A16C0E80FF0780AE2A16BD4331EFB409F625B43D92B2F4E0A6310D9CA77FB9A",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))));
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.EventRecordConversion", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries).EmittedClrMethod);
    }
}
