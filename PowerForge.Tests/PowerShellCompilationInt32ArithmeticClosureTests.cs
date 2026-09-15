namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("net10.0")]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Int32Arithmetic_ClosedPromotionAndCallConsumersMatchPowerShell(string framework)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-Sum { [OutputType([int])] param([int]$Left,[int]$Right) return $Left + $Right }
            function Get-Difference { [OutputType([int])] param([int]$Left,[int]$Right) return $Left - $Right }
            function Get-Product { [OutputType([int])] param([int]$Left,[int]$Right) return $Left * $Right }
            function Get-MiddleSum { [OutputType([int])] param([int]$Left,[int]$Right) return Get-Sum $Left $Right }
            function Test-PositiveSum { [OutputType([bool])] param([int]$Left,[int]$Right) return (Get-MiddleSum $Left $Right) -gt 0 }
            function Get-IntegerDifference { [OutputType([int])] param([int]$Left,[int]$Right) [int]$value = Get-Difference $Left $Right; return $value }
            function Get-IntegerCast { [OutputType([int])] param([int]$Left,[int]$Right) return [int]($Left - $Right) }
            function Get-RoundedHalf { [OutputType([int])] param([int]$Left) $value = 0; $value += $Left; return [int]($value / 2) }
            function Get-PreservedInteger { [OutputType([int])] param([int]$Left,[int]$Right) [int]$value = 17; try { $value = Get-Difference $Left $Right } catch [OverflowException] { return $value }; return $value }
            function Get-RecursiveSum { [OutputType([int])] param([int]$Value,[int]$Depth) if($Depth -le 0) { return $Value + 1 }; $Depth--; return Get-RecursiveSum $Value $Depth }
            """, ".psm1");
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "ClosedArithmetic", PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        Assert.False(build.Manifest!.RequiresPowerShellRuntime);
        Assert.Equal(10, build.Manifest.CompiledMethods);
        var type = System.Reflection.Assembly.LoadFile(build.ArtifactPath!).GetType(
            build.Manifest.PublicAbi!.NamespaceName + "." + build.Manifest.PublicAbi.TypeName)!;
        var observations = new List<string>();
        foreach (var left in new[] { int.MinValue, -46341, -1, 0, 1, 46341, int.MaxValue })
        foreach (var right in new[] { int.MinValue, -46341, -1, 0, 1, 46341, int.MaxValue })
        foreach (var method in new[] { "Get_Sum", "Get_Difference", "Get_Product", "Test_PositiveSum" })
        {
            var result = type.GetMethod(method)!.Invoke(null, new object[] { left, right })!;
            observations.Add(method + ":" + left + ":" + right + ":" + result.GetType().FullName + ":" +
                Convert.ToString(result is double number ? BitConverter.DoubleToInt64Bits(number) : result,
                    System.Globalization.CultureInfo.InvariantCulture));
        }
        foreach (var name in new[] { "Get_IntegerDifference", "Get_IntegerCast" })
        {
            var method = type.GetMethod(name)!;
            Assert.Equal(7, method.Invoke(null, new object[] { 10, 3 }));
            var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(null, new object[] { int.MinValue, 1 }));
            Assert.IsType<OverflowException>(error.InnerException);
            Assert.Equal(-7, method.Invoke(null, new object[] { 3, 10 }));
        }
        var half = type.GetMethod("Get_RoundedHalf")!;
        foreach (var value in new[] { -7, -5, -3, -1, 0, 1, 3, 5, 7 })
            observations.Add("half:" + value + ":" + half.Invoke(null, new object[] { value }));
        var preserved = type.GetMethod("Get_PreservedInteger")!;
        Assert.Equal(17, preserved.Invoke(null, new object[] { int.MinValue, 1 }));
        Assert.Equal(7, preserved.Invoke(null, new object[] { 10, 3 }));
        foreach (var value in new[] { int.MinValue, -1, 0, int.MaxValue })
        foreach (var depth in new[] { 0, 1, 5 })
        {
            var result = type.GetMethod("Get_RecursiveSum")!.Invoke(null, new object[] { value, depth })!;
            observations.Add("recursive:" + value + ":" + depth + ":" + result.GetType().FullName + ":" +
                Convert.ToString(result is double number ? BitConverter.DoubleToInt64Bits(number) : result,
                    System.Globalization.CultureInfo.InvariantCulture));
        }
        foreach (var configuration in StatementErrorHosts())
        {
            var original = RunStatementErrorProbe((string)configuration[1],
                "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "' -DisableNameChecking; " + """
                foreach($left in [int]::MinValue,-46341,-1,0,1,46341,[int]::MaxValue) {
                    foreach($right in [int]::MinValue,-46341,-1,0,1,46341,[int]::MaxValue) {
                        foreach($method in 'Get-Sum','Get-Difference','Get-Product','Test-PositiveSum') {
                            $result = & $method $left $right
                            $kind = $result.GetType().FullName
                            if($result -is [double]) { $result = [BitConverter]::DoubleToInt64Bits($result) }
                            $method.Replace('-','_') + ':' + $left + ':' + $right + ':' + $kind + ':' + [Convert]::ToString($result,[Globalization.CultureInfo]::InvariantCulture)
                        }
                    }
                }
                foreach($value in -7,-5,-3,-1,0,1,3,5,7) { 'half:' + $value + ':' + (Get-RoundedHalf $value) }
                foreach($value in [int]::MinValue,-1,0,[int]::MaxValue) {
                    foreach($depth in 0,1,5) {
                        $result = Get-RecursiveSum $value $depth
                        $kind = $result.GetType().FullName
                        if($result -is [double]) { $result = [BitConverter]::DoubleToInt64Bits($result) }
                        'recursive:' + $value + ':' + $depth + ':' + $kind + ':' + [Convert]::ToString($result,[Globalization.CultureInfo]::InvariantCulture)
                    }
                }
                """, fixture.RootPath, "closed-arithmetic-original-" + configuration[0]);
            Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
            Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
            Assert.Equal(observations, original.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
