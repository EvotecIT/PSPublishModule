using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void HostEnumConversion_PreservesLoadedMissingTypesOperandOrderErrorsAndReuse(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-HostEnum {
                [CmdletBinding()]param($Value,$Trace,[switch]$System)
                $copy='prior'
                try {
                    if($System){$copy=[Microsoft.PowerShell.Commands.PCSystemType]$($Trace.Add('operand');$Value)}
                    else{$copy=[Microsoft.PowerShell.Commands.OSProductSuite]$($Trace.Add('operand');$Value)}
                }catch{[pscustomobject]@{error=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
                finally{$Trace.Add('finally')}
                [pscustomobject]@{copy=$copy;type=$copy.GetType().FullName;trace=@($Trace.ToArray());text=[string]$copy}
            }
            function Read-SimpleHostEnum {[CmdletBinding()]param($Value);[Microsoft.PowerShell.Commands.PCSystemType]$Value}
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, Path.Combine(fixture.OutputPath, "module's folder with spaces"), "Generated.HostEnum", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries.Where(unit => unit.Kind == PowerShellCompilationUnitKind.Function), unit =>
        {
            Assert.True(unit.EmittedClrMethod);
            Assert.True(unit.UsesNativeFunctionBinding);
            Assert.False(unit.RetainedHostedSource);
        });
        const string probe = """
            foreach($loaded in $false,$true){
                if($loaded){Import-Module Microsoft.PowerShell.Management -ErrorAction Stop}
                foreach($system in $false,$true){
                    foreach($case in @(@{value=$null},@{value=1},@{value='1'},@{value=65535},@{value='bad'},@{value=1})){
                        $trace=[Collections.Generic.List[string]]::new()
                        $records=@(Read-HostEnum -Value $case.value -Trace $trace -System:$system)
                        [pscustomobject]@{loaded=$loaded;system=$system;value=$case.value;records=$records}|ConvertTo-Json -Depth 10 -Compress
                    }
                }
            }
            Add-Type -TypeDefinition @'
            using System;
            using System.ComponentModel;
            using System.Globalization;
            using System.Collections.Generic;
            [TypeConverter(typeof(HostEnumTokenConverter))]
            public sealed class HostEnumToken { public bool Fail; }
            public sealed class HostEnumTokenConverter : TypeConverter {
                public static readonly List<string> Trace = new List<string>();
                public override bool CanConvertTo(ITypeDescriptorContext context, Type destination) { return destination.IsEnum; }
                public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destination) {
                    Trace.Add(destination.FullName);
                    if (((HostEnumToken)value).Fail) throw new InvalidOperationException("offline-enum-callback-failure");
                    return Enum.ToObject(destination, 1);
                }
            }
            '@
            foreach($system in $false,$true){foreach($fail in $false,$true){
                [HostEnumTokenConverter]::Trace.Clear()
                $token=New-Object HostEnumToken;$token.Fail=$fail
                $trace=[Collections.Generic.List[string]]::new()
                $records=@(Read-HostEnum -Value $token -Trace $trace -System:$system)
                [pscustomobject]@{system=$system;fail=$fail;records=$records;callbacks=@([HostEnumTokenConverter]::Trace.ToArray())}|ConvertTo-Json -Depth 10 -Compress
            }}
            [pscustomobject]@{simple=@(Read-SimpleHostEnum -Value 1 2>&1 | ForEach-Object {
                if($_ -is [Management.Automation.ErrorRecord]){
                    [pscustomobject]@{error=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message}
                }else{[pscustomobject]@{type=$_.GetType().FullName;value=[int]$_}}
            })}|ConvertTo-Json -Depth 10 -Compress
            """;
        // ConsoleHost replays a startup JIT profile that can load Management in the background.
        // Keep the cold phase cold in each child: .NET 10 rejects profile replay below this CPU gate.
        // https://github.com/dotnet/runtime/blob/v10.0.0/src/coreclr/vm/multicorejit.cpp#L1075-L1103
        var environment = new Dictionary<string, string>
        {
            ["DOTNET_MultiCoreJitMinNumCpus"] = uint.MaxValue.ToString("x", System.Globalization.CultureInfo.InvariantCulture)
        };
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "host-enum-original", environment);
        var generated = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "host-enum-generated", environment);
        Assert.True(original.ExitCode == 0 && generated.ExitCode == 0, original.StandardError + generated.StandardError);
        var expected = original.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var actual = generated.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(29, expected.Length);
        Assert.Equal(expected.Length, actual.Length);
        for(var i=0;i<29;i++) Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected[i]), JsonNode.Parse(actual[i])),
            "Original: " + expected[i] + Environment.NewLine + "Generated: " + actual[i]);
        Assert.Equal(original.StandardError, generated.StandardError);
        var simple = Assert.Single(JsonNode.Parse(actual[28])!["simple"]!.AsArray())!;
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("Microsoft.PowerShell.Commands.PCSystemType", simple["type"]!.GetValue<string>());
            Assert.Equal(1, simple["value"]!.GetValue<int>());
            for (var i = 0; i < 12; i++)
            {
                var record = JsonNode.Parse(actual[i])!;
                Assert.Contains("TypeNotFound", record["records"]![0]!["error"]!.GetValue<string>(), StringComparison.Ordinal);
                Assert.Equal("prior", record["records"]![1]!["copy"]!.GetValue<string>());
                Assert.Equal(new[] { "finally" }, record["records"]![1]!["trace"]!.AsArray()
                    .Select(static value => value!.GetValue<string>()));
            }
            Assert.Contains("Microsoft.PowerShell.Commands.OSProductSuite", actual[24]);
            Assert.Single(JsonNode.Parse(actual[24])!["callbacks"]!.AsArray());
            Assert.Single(JsonNode.Parse(actual[25])!["callbacks"]!.AsArray());
            Assert.Contains("offline-enum-callback-failure", actual[25]);
            Assert.Contains("operand", actual[13]);
        }
        else
        {
            // These Management enums are absent on Unix even after module import.
            // Resolution must fail before operand evaluation or converter callbacks.
            Assert.Contains("TypeNotFound", simple["error"]!.GetValue<string>(), StringComparison.Ordinal);
            for (var i = 0; i < 28; i++)
            {
                var record = JsonNode.Parse(actual[i])!;
                Assert.Contains("TypeNotFound", record["records"]![0]!["error"]!.GetValue<string>(), StringComparison.Ordinal);
                Assert.Equal("prior", record["records"]![1]!["copy"]!.GetValue<string>());
                Assert.Equal(new[] { "finally" }, record["records"]![1]!["trace"]!.AsArray()
                    .Select(static value => value!.GetValue<string>()));
                if (i >= 24) Assert.Empty(record["callbacks"]!.AsArray());
            }
        }
        Assert.Contains("finally", actual[13]);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void HostEnumConversion_PreservesStrictAndUnqualifiedUnknownTypeBoundaries(string framework)
    {
        var source = PowerShellSourceParser.Parse("""
            function Read-Enum {[CmdletBinding()]param($Value);[Microsoft.PowerShell.Commands.PCSystemType]$Value}
            function Read-Unknown {[CmdletBinding()]param($Value);[Missing.Authored.Enum]$Value}
            """, Path.Combine(Path.GetTempPath(), "host-enum-boundaries.ps1"));
        var hybrid = new PowerShellSemanticCompilationPipeline().Compile(new[] {source}, framework, PowerShellCompilationCapabilities.HybridModule);
        var method = Assert.Single(hybrid.Emitted.Methods);
        Assert.Equal("Read_Enum",method.GeneratedName);
        Assert.NotNull(method.NativeFunctionBinding);
        var strict = new PowerShellSemanticCompilationPipeline().Compile(new[] {source}, framework, PowerShellCompilationCapabilities.TypedLibrary);
        Assert.Empty(strict.Emitted.Methods);
    }
}
