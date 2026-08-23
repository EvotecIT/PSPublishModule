using System;
using System.IO;
using System.Linq;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

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
        Assert.Contains("double relativeCap = (BaselineMs * ((1D + RelativeTolerance)));", result.SourceCode, StringComparison.Ordinal);
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
        Assert.Contains("for (int i = 1; (i <= Count); i++)", result.SourceCode, StringComparison.Ordinal);
        Assert.Contains("total += i;", result.SourceCode, StringComparison.Ordinal);
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
