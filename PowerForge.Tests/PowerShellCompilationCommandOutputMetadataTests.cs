using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CommandOutput_SeparatesRecordMetadataFromVoidMethodReturns(string framework, string host)
    {
        const string source = """
            function Get-Items { [CmdletBinding()] param(); return [System.Collections.ArrayList]::new() }
            function Get-Map { [CmdletBinding()] param(); return [System.Collections.Hashtable]::new() }
            function Get-CalledMap { [CmdletBinding()] param(); Get-Map }
            function Get-CapturedNumber { [CmdletBinding()] param(); $result=$null; $result=for ($i=0;$i -lt 2;$i++) { $i }; return 'done' }
            function Get-DiscardedCapture { [CmdletBinding()] param(); $result=$null; $result=for ($i=0;$i -lt 2;$i++) { $i } }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var semantic = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { PowerShellSourceParser.Parse(source, fixture.ScriptPath) }, framework,
            PowerShellCompilationCapabilities.BinaryModule);
        var calledMap = Assert.Single(semantic.Analyzed.Functions, function => function.Symbol.Name == "Get-CalledMap");
        Assert.Equal(typeof(System.Collections.Hashtable), PowerShellSuccessOutputTypePolicy.Resolve(
            calledMap, semantic.Analyzed.Functions.ToDictionary(function => function.Symbol.StableKey, StringComparer.Ordinal)));
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.CommandOutputMetadata", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(5, result.Manifest!.CompiledMethods);
        const string proof = """
            foreach ($name in 'Get-Items','Get-Map','Get-CalledMap','Get-CapturedNumber','Get-DiscardedCapture') {
                $records=@(& $name)
                [pscustomobject]@{name=$name;records=$records;count=$records.Count} | ConvertTo-Json -Compress -Depth 6
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + proof,
            fixture.RootPath, "original-output-metadata");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + proof,
            fixture.RootPath, "compiled-output-metadata");
        Assert.Equal(0, original.ExitCode);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
        var metadata = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + """
            foreach ($name in 'Get-Items','Get-Map','Get-CalledMap','Get-CapturedNumber','Get-DiscardedCapture') {
                $name + ':' + ((Get-Command $name).OutputType.Type.FullName -join ',')
            }
            """, fixture.RootPath, "command-output-metadata");
        Assert.True(metadata.ExitCode == 0, metadata.StandardOutput + metadata.StandardError);
        Assert.Equal(new[] { "Get-Items:System.Object", "Get-Map:System.Collections.Hashtable", "Get-CalledMap:System.Collections.Hashtable",
            "Get-CapturedNumber:System.String", "Get-DiscardedCapture:" },
            metadata.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
    }
}
