using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NumericUnion_PreservesSequentialCommandRecords(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-NumericRecords {
                [CmdletBinding()] param([int]$Delta)
                $Value = [int]2147483647
                $Value
                $Value += $Delta
                $Value
                $Value--
                return $Value
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NumericRecords", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        const string probe = """
            foreach ($delta in -1,0,1,2) {
                $records=@(Get-NumericRecords $delta | ForEach-Object { $_.GetType().FullName + ':' + $_ })
                [pscustomobject]@{ delta=$delta; records=$records } | ConvertTo-Json -Compress
            }
            $first=@(Get-NumericRecords 1 | Select-Object -First 1)
            [pscustomobject]@{ count=$first.Count; type=$first[0].GetType().FullName; value=$first[0] } | ConvertTo-Json -Compress
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-numeric-records");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-numeric-records");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NumericUnion_PreservesUnconstrainedValuesAndTypesInRuntimeFreeLibrary(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-Added { param([int]$Delta) $Value = [int]2147483647; $Value += $Delta; return $Value }
            function Get-Subtracted { param([int]$Delta) $Value = [int]-2147483648; $Value -= $Delta; return $Value }
            function Get-Multiplied { param([int]$Delta) $Value = [int]2147483647; $Value *= $Delta; return $Value }
            function Get-Incremented { param([int]$Delta) $Value = [int]2147483647; for ([int]$Step=0; $Step -lt $Delta; $Step++) { $Value++ }; return $Value }
            function Get-PromotedThenReduced { param([int]$Delta) $Value = [int]2147483647; $Value++; $Value -= $Delta; return $Value }
            function Get-Compared { param([int]$Delta) $Value = [int]2147483647; $Value += $Delta; return $Value -gt 2147483647 }
            function Get-Aliased { param([int]$Delta) $Value = [int]2147483647; $Value++; $Copy=$Value; $Value += $Delta; return $Copy }
            function Get-Reassigned { param([int]$Delta) $Value = [int]2147483647; $Value++; $Value=7; $Value -= $Delta; return $Value }
            function Get-TypeName { param([int]$Delta) $Value = [int]2147483647; $Value += $Delta; return $Value.GetType().FullName }
            function Get-ParameterCopy { param([int]$Delta) $Value=$Delta; $Value += 2147483647; return $Value }
            function Get-MixedAdd { param([double]$Delta) $Value=2147483647; $Value++; return $Value + $Delta }
            function Get-MixedSubtract { param([double]$Delta) $Value=2147483647; $Value++; return $Delta - $Value }
            function Get-MixedProduct { param([double]$Delta) $Value=2147483647; $Value++; return $Value * $Delta }
            function Get-MixedDivision { param([double]$Delta) $Value=2147483647; $Value++; return $Value / $Delta }
            function Get-MixedRemainder { param([double]$Delta) $Value=2147483647; $Value++; return $Value % $Delta }
            function Get-MixedCompare { param([double]$Delta) $Value=2147483647; $Value++; return $Value -ge $Delta }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NumericUnion", PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
        Assert.Equal(16, result.Manifest.CompiledMethods);
        const string cases = """
            foreach ($name in 'Added','Subtracted','Multiplied','Incremented','PromotedThenReduced','Compared','Aliased','Reassigned','TypeName','ParameterCopy',
                'MixedAdd','MixedSubtract','MixedProduct','MixedDivision','MixedRemainder','MixedCompare') {
                $deltas=if ($name.StartsWith('Mixed')) { @(-1.25,0.0,[double]::Epsilon,[double]::NaN,[double]::NegativeInfinity,[double]::PositiveInfinity,[double]::MaxValue) } else { @(-2,0,1,2,3) }
                foreach ($delta in $deltas) {
                    $value = Invoke-Case $name $delta
                    [pscustomobject]@{ name=$name; delta=$delta; type=$value.GetType().FullName; value=$value } | ConvertTo-Json -Compress
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "'; function Invoke-Case($name,$delta) { & ('Get-'+$name) $delta }; " + cases, fixture.RootPath, "original-numeric-union");
        var compiled = RunStatementErrorProbe(host, "$assembly=[Reflection.Assembly]::LoadFrom('" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "'); $type=$assembly.GetTypes() | Where-Object { $null -ne $_.GetMethod('Get_Added') }; " +
            "function Invoke-Case($name,$delta) { $argument=if($name.StartsWith('Mixed')){[double]$delta}else{[int]$delta}; $type.GetMethod('Get_'+$name).Invoke($null,[object[]]@($argument)) }; " + cases,
            fixture.RootPath, "compiled-numeric-union");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        if (framework == "net10.0") VerifyNumericUnionFromIndependentConsumer(fixture.RootPath, result);
    }

    private static void VerifyNumericUnionFromIndependentConsumer(string root, PowerShellCompilationBuildResult result)
    {
        var directory = Path.Combine(root, "consumer");
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "NumericConsumer.csproj");
        var project = new System.Xml.Linq.XElement("Project", new System.Xml.Linq.XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new System.Xml.Linq.XElement("PropertyGroup",
                new System.Xml.Linq.XElement("TargetFramework", "net10.0"),
                new System.Xml.Linq.XElement("OutputType", "Exe"),
                new System.Xml.Linq.XElement("ImplicitUsings", "enable")),
            new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("Reference",
                new System.Xml.Linq.XAttribute("Include", "GeneratedNumericUnion"),
                new System.Xml.Linq.XElement("HintPath", result.ArtifactPath!))));
        new System.Xml.Linq.XDocument(project).Save(projectPath);
        var abi = result.Manifest!.PublicAbi!;
        File.WriteAllText(Path.Combine(directory, "Program.cs"),
            "using Methods = global::" + abi.NamespaceName + "." + abi.TypeName + ";\n" + """
            object promoted = Methods.Get_Added(1);
            if (promoted is not double number || number != 2147483648d) return 1;
            if (Methods.Get_Reassigned(3) is not int restored || restored != 4) return 2;
            if (Methods.Get_TypeName(1) != "System.Double") return 3;
            if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "System.Management.Automation")) return 4;
            Console.WriteLine("runtime-free numeric consumer passed");
            return 0;
            """);
        var build = RunProcess("dotnet", "build", projectPath, "-c", "Release", "--nologo", "-v:q");
        Assert.True(build.ExitCode == 0, build.StandardOutput + build.StandardError);
        var run = RunProcess("dotnet", Path.Combine(directory, "bin", "Release", "net10.0", "NumericConsumer.dll"));
        Assert.True(run.ExitCode == 0, run.StandardOutput + run.StandardError);
        Assert.Contains("runtime-free numeric consumer passed", run.StandardOutput, StringComparison.Ordinal);
    }
}
