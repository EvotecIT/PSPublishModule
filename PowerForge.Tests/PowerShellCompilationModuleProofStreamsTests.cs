namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    public static IEnumerable<object[]> ModuleProofHosts()
        => StatementErrorHosts().Select(configuration => new[] { configuration[1] });

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(ModuleProofHosts))]
    public void ModuleProof_DrainsBothRedirectedStreamsBeforeReportingFailure(string host)
    {
        using var fixture = ArtifactFixture.Create("function Get-ProofValue { 'value' }", ".psm1");
        // This exceeds either pipe buffer. Sequential stdout/stderr reads block
        // before the timeout when the child fills stderr before closing stdout.
        var failure = Record.Exception(() => RunModuleProof(fixture.ScriptPath, """
            [Console]::Out.WriteLine('stdout-before')
            [Console]::Error.WriteLine('stderr-before' + ('x' * 100000))
            [Console]::Out.WriteLine('stdout-after' + ('y' * 100000))
            [Console]::Error.WriteLine('stderr-after')
            """, host));
        Assert.NotNull(failure);
        Assert.Contains("stderr-before", failure.Message);
        Assert.Contains("stderr-after", failure.Message);
    }
}
