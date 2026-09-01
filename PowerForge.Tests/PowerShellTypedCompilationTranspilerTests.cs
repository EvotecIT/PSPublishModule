using System;
using System.IO;
using System.Linq;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellTypedCompilationTranspilerTests
{
    [Fact]
    public void Transpile_EmitsRealWorldTypedArithmeticFunction()
    {
        using var fixture = TranspilerFixture.Create(
            """
            function Get-AllowedAverageMs {
                param(
                    [double] $BaselineMs,
                    [double] $RelativeTolerance,
                    [double] $AbsoluteToleranceMs
                )

                $relativeCap = $BaselineMs * (1.0 + $RelativeTolerance)
                $absoluteCap = $BaselineMs + $AbsoluteToleranceMs
                if ($relativeCap -gt $absoluteCap) {
                    return $relativeCap
                }
                return $absoluteCap
            }
            """);

        var result = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath);

        var method = Assert.Single(result.Methods);
        Assert.Equal("Get-AllowedAverageMs", method.SourceName);
        Assert.Equal("Get_AllowedAverageMs", method.GeneratedName);
        Assert.Equal(typeof(double).FullName, method.ReturnType);
        Assert.Empty(result.Diagnostics);
        Assert.Contains("public static double Get_AllowedAverageMs(double BaselineMs, double RelativeTolerance, double AbsoluteToleranceMs)", result.SourceCode, StringComparison.Ordinal);
        Assert.Contains("double relativeCap = (BaselineMs * (1d + RelativeTolerance));", result.SourceCode, StringComparison.Ordinal);
        Assert.Contains("if ((relativeCap > absoluteCap))", result.SourceCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_EmitsTypedForLoopAndNumericWidening()
    {
        using var fixture = TranspilerFixture.Create(
            """
            function Get-TriangularNumber {
                param([int] $Count)
                [long] $total = 0
                for ([int] $i = 1; $i -le $Count; $i++) {
                    $total += $i
                }
                return $total
            }
            """);

        var result = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath);

        var method = Assert.Single(result.Methods);
        Assert.Equal(typeof(long).FullName, method.ReturnType);
        Assert.Empty(result.Diagnostics);
        Assert.Contains("for (", result.SourceCode, StringComparison.Ordinal);
        Assert.Contains("i <= Count", result.SourceCode, StringComparison.Ordinal);
        Assert.Contains("total = checked((long)(total + i));", result.SourceCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_LeavesUnsupportedFunctionOutAndReturnsItsBlockers()
    {
        using var fixture = TranspilerFixture.Create(
            """
            function Get-Thing {
                param([string] $Path)
                return Get-Item -LiteralPath $Path
            }
            """);

        var result = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath);

        Assert.Empty(result.Methods);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == PowerShellCompilationDiagnosticCode.CommandInvocation);
        Assert.False(result.Success);
    }

    [Fact]
    public void Transpile_RejectsExceptionConstructionConversionWithoutEmittingInvalidCSharp()
    {
        using var fixture = TranspilerFixture.Create(
            "function Invoke-Failure { throw [System.ArgumentException] 'expected' }");

        var result = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath);

        Assert.Empty(result.Methods);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("requires the PowerShell language-conversion runtime", StringComparison.Ordinal));
        Assert.DoesNotContain("System.ArgumentException: expected", result.SourceCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_EmitsPowerShellNegativeIndexNormalizationForArrays()
    {
        using var fixture = TranspilerFixture.Create(
            "function Get-Value { param([int[]] $Values, [int] $Index); return $Values[$Index] }");

        var result = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath);

        var method = Assert.Single(result.Methods);
        Assert.Equal(typeof(object).FullName, method.ReturnType);
        Assert.Empty(result.Diagnostics);
        Assert.Contains("? null : (object)", result.SourceCode, StringComparison.Ordinal);
        Assert.Contains(".Length + (Index)", result.SourceCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_RoutesArrayConcatenationCompoundAssignmentToFallback()
    {
        using var fixture = TranspilerFixture.Create(
            "function Add-Values { param([int[]] $Values, [int[]] $Other); $Values += $Other; return $Values }");

        var result = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath);

        Assert.Empty(result.Methods);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("compound assignment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Transpile_RoutesInvalidSignedUnsignedCompoundAssignmentToFallback()
    {
        using var fixture = TranspilerFixture.Create(
            "function Add-Value { param([ulong] $Total, [long] $Value); $Total += $Value; return $Total }");

        var result = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath);

        Assert.Empty(result.Methods);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("compound assignment", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("[int] $Total = 0; $Total += 1.9")]
    [InlineData("[long] $Total = 10; $Total -= 0.5")]
    [InlineData("[int] $Total = 3; $Total /= 2")]
    public void Transpile_RoutesIntegralCompoundAssignmentsWithDifferentPowerShellConversionToFallback(string operation)
    {
        using var fixture = TranspilerFixture.Create(
            $"function Update-Value {{ {operation}; return $Total }}");

        var result = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath);

        Assert.Empty(result.Methods);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("compound assignment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Transpile_RoutesNullableArrayMembersOtherThanLengthToFallback()
    {
        using var fixture = TranspilerFixture.Create(
            "function Get-ArrayRank { param([int[]] $Values); return $Values.Rank }");

        var result = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath);

        Assert.Empty(result.Methods);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("null-member semantics", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Transpile_RoutesStructEqualityWithoutClrOperatorToFallback()
    {
        using var fixture = TranspilerFixture.Create(
            "function Test-Entry { $left = [System.Collections.DictionaryEntry]::new('key', 'value'); $right = [System.Collections.DictionaryEntry]::new('key', 'value'); return $left -eq $right }");

        var result = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath);

        Assert.Empty(result.Methods);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("equality operator", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("string", "$Value++")]
    [InlineData("string", "++$Value")]
    [InlineData("string", "$Value--")]
    [InlineData("string", "--$Value")]
    [InlineData("bool", "$Value++")]
    [InlineData("bool", "++$Value")]
    [InlineData("bool", "$Value--")]
    [InlineData("bool", "--$Value")]
    [InlineData("int[]", "$Value++")]
    [InlineData("int[]", "++$Value")]
    [InlineData("int[]", "$Value--")]
    [InlineData("int[]", "--$Value")]
    public void Transpile_RoutesIncrementForNonNumericClrTypeToFallback(string type, string operation)
    {
        using var fixture = TranspilerFixture.Create(
            $"function Update-Value {{ param([{type}] $Value); for (; $false; {operation}) {{ }}; return $Value }}");

        var result = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath);

        Assert.Empty(result.Methods);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.FeatureId == "operator.increment");
    }

    [Fact]
    public void Transpile_RoutesSpecialNameClrMethodInvocationToFallback()
    {
        using var fixture = TranspilerFixture.Create(
            "function Add-Decimal { return [decimal]::op_Addition([decimal]::One, [decimal]::One) }");

        var result = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath);

        Assert.Empty(result.Methods);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("No exact CLR overload", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("return [System.Management.Automation.PSVersionInfo]::PSEdition")]
    [InlineData("return [System.Collections.Concurrent.ConcurrentDictionary[string,int]]::new()")]
    public void Transpile_RejectsTypesUnavailableToGeneratedRuntimeIndependentProjects(string body)
    {
        using var fixture = TranspilerFixture.Create($"function Get-Value {{ {body} }}");

        var result = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath);

        Assert.Empty(result.Methods);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("reference set", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("net472")]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public void Transpile_EmitsExactConstructedGenericListMembersAvailableToTarget(string targetFramework)
    {
        using var fixture = TranspilerFixture.Create(
            "function Get-Count { $items = [System.Collections.Generic.List[string]]::new(); $items.AddRange([string[]] ('alpha', 'beta')); $copy = $items.ToArray(); return $copy.Length }");

        var result = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath, targetFramework);

        Assert.Empty(result.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        Assert.Single(result.Methods);
        Assert.Contains("List<string> items = new global::System.Collections.Generic.List<string>();", result.SourceCode, StringComparison.Ordinal);
        Assert.Contains("(items).AddRange", result.SourceCode, StringComparison.Ordinal);
        Assert.Contains("string[] copy = (items).ToArray();", result.SourceCode, StringComparison.Ordinal);
        Assert.Contains("return (copy ?? global::System.Array.Empty<string>()).Length;", result.SourceCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_NormalizesNullCountForExplicitConstructedGenericListParameter()
    {
        using var fixture = TranspilerFixture.Create(
            "function Get-Count { param([System.Collections.Generic.List[string]] $Items) return $Items.Count }");

        var result = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath, "net10.0");

        Assert.Empty(result.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        Assert.Single(result.Methods);
        Assert.Contains("?.Count ?? 0", result.SourceCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_RoutesGenericMemberReturnTypeToDiagnosticInsteadOfThrowing()
    {
        using var fixture = TranspilerFixture.Create(
            "function Get-Converters { return ([System.Text.Json.JsonSerializerOptions]::new()).Converters }");

        var exception = Record.Exception(() => new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath));
        Assert.Null(exception);
        var result = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath);
        Assert.Empty(result.Methods);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.FeatureId == "syntax.memberexpression");
    }

    [Fact]
    public void Transpile_EmitsSupportedExplicitGenericLocalTypeWithoutThrowing()
    {
        using var fixture = TranspilerFixture.Create(
            "function Get-Items { param([int[]] $Values); [System.Collections.Generic.IEnumerable[int]] $items = $Values; return $Values }");

        var exception = Record.Exception(() => new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath));
        Assert.Null(exception);
        var result = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath);
        Assert.Single(result.Methods);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ResolveDotNetRoot_IgnoresInvalidConfiguredRootAndFindsSdkOnPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var sdkRoot = Path.Combine(root, "sdk");
        var packs = Path.Combine(sdkRoot, "packs", "Microsoft.NETCore.App.Ref");
        Directory.CreateDirectory(packs);
        File.WriteAllText(Path.Combine(sdkRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"), string.Empty);
        try
        {
            var resolved = PowerShellGeneratedTypePolicy.ResolveDotNetRoot(Path.Combine(root, "invalid"), new[] { sdkRoot });
            Assert.Equal(Path.GetFullPath(sdkRoot), resolved);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ResolveDotNetRoot_FollowsPosixExecutableSymlink()
    {
        if (OperatingSystem.IsWindows())
            return;
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var sdkRoot = Path.Combine(root, "sdk");
        var bin = Path.Combine(root, "bin");
        Directory.CreateDirectory(Path.Combine(sdkRoot, "packs", "Microsoft.NETCore.App.Ref"));
        Directory.CreateDirectory(bin);
        var target = Path.Combine(sdkRoot, "dotnet");
        File.WriteAllText(target, string.Empty);
        File.CreateSymbolicLink(Path.Combine(bin, "dotnet"), target);
        try
        {
            var resolved = PowerShellGeneratedTypePolicy.ResolveDotNetRoot(null, new[] { bin });
            Assert.Equal(Path.GetFullPath(sdkRoot), resolved);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ResolveDotNetRoot_UsesSdkListForRegularExecutableShim()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var sdkRoot = Path.Combine(root, "sdk-root");
        var shimDirectory = Path.Combine(root, "shim");
        Directory.CreateDirectory(Path.Combine(sdkRoot, "packs", "Microsoft.NETCore.App.Ref"));
        Directory.CreateDirectory(Path.Combine(sdkRoot, "sdk"));
        Directory.CreateDirectory(shimDirectory);
        var shim = Path.Combine(shimDirectory, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        File.WriteAllText(shim, string.Empty);
        try
        {
            var resolved = PowerShellGeneratedTypePolicy.ResolveDotNetRoot(
                configured: null,
                pathDirectories: new[] { shimDirectory },
                sdkListProbe: executable =>
                {
                    Assert.Equal(Path.GetFullPath(shim), executable);
                    return $"10.0.100 [{Path.Combine(sdkRoot, "sdk")}]";
                });

            Assert.Equal(Path.GetFullPath(sdkRoot), resolved);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private sealed class TranspilerFixture : IDisposable
    {
        private TranspilerFixture(string rootPath, string scriptPath)
        {
            RootPath = rootPath;
            ScriptPath = scriptPath;
        }

        public string RootPath { get; }
        public string ScriptPath { get; }

        public static TranspilerFixture Create(string source)
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            var scriptPath = Path.Combine(rootPath, "input.ps1");
            File.WriteAllText(scriptPath, source);
            return new TranspilerFixture(rootPath, scriptPath);
        }

        public void Dispose()
        {
            try { Directory.Delete(RootPath, recursive: true); } catch { }
        }
    }
}
