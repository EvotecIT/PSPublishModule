using System;
using System.IO;
using System.Linq;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed class PowerShellCompilationAnalyzerTests
{
    [Fact]
    public void Analyze_AcceptsTypedArithmeticLoopAsWholeFunction()
    {
        using var fixture = CompilationFixture.Create(
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

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.Equal("Get-TriangularNumber", unit.Name);
        Assert.True(unit.IsCompilable, string.Join(Environment.NewLine, unit.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.Equal(typeof(int).FullName, Assert.Single(unit.Parameters).TypeName);
        Assert.Equal(1, plan.CompilableUnits);
        Assert.Equal(100d, plan.CompilationCoveragePercentage);
    }

    [Fact]
    public void Analyze_ReportsCommandAndUntypedParameterWithoutPartialCompilationClaim()
    {
        using var fixture = CompilationFixture.Create(
            """
            function Get-Thing {
                param($Path)
                Get-Item -LiteralPath $Path
            }
            """);

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath, PowerShellCompilationMode.Hybrid));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, static diagnostic => diagnostic.Code == PowerShellCompilationDiagnosticCode.UnsupportedParameterType);
        Assert.Contains(unit.Diagnostics, static diagnostic => diagnostic.Code == PowerShellCompilationDiagnosticCode.CommandInvocation && diagnostic.Message.Contains("Get-Item", StringComparison.Ordinal));
        Assert.Equal(1, plan.RuntimeFallbackUnits);
        Assert.True(plan.CanProceed);
    }

    [Fact]
    public void Analyze_StrictModeRejectsRuntimeScopeAndNestedScriptBlock()
    {
        using var fixture = CompilationFixture.Create(
            """
            function Invoke-Thing {
                param([int] $Value)
                $path = $env:TEMP
                $items = { $env:TEMP }
                return $Value
            }
            """);

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath, PowerShellCompilationMode.Strict));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.Contains(unit.Diagnostics, static diagnostic => diagnostic.Code == PowerShellCompilationDiagnosticCode.ScriptBlock);
        Assert.Contains(unit.Diagnostics, static diagnostic => diagnostic.Code == PowerShellCompilationDiagnosticCode.RuntimeScope);
        Assert.False(plan.CanProceed);
    }

    [Fact]
    public void Analyze_ReportsParserErrorsAtFileLevel()
    {
        using var fixture = CompilationFixture.Create("function Broken-Thing {");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        var file = Assert.Single(plan.Files);
        Assert.Empty(file.Units);
        Assert.Contains(file.Diagnostics, static diagnostic => diagnostic.Code == PowerShellCompilationDiagnosticCode.ParseError);
        Assert.Equal(1, plan.ParseErrorFiles);
        Assert.False(plan.CanProceed);
    }

    [Fact]
    public void Analyze_DoesNotIgnoreProcessBlockOrParameterDefaultSemantics()
    {
        using var fixture = CompilationFixture.Create(
            """
            function Invoke-StreamingThing {
                param([Parameter(Mandatory)] [int] $Value = (Get-Random))
                process {
                    $Value + 1
                }
            }
            """);

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, static diagnostic => diagnostic.Code == PowerShellCompilationDiagnosticCode.UnsupportedSyntax && diagnostic.Message.Contains("process", StringComparison.Ordinal));
        Assert.Contains(unit.Diagnostics, static diagnostic => diagnostic.Code == PowerShellCompilationDiagnosticCode.UnsupportedSyntax && diagnostic.Message.Contains("AttributeAst", StringComparison.Ordinal));
        Assert.Contains(unit.Diagnostics, static diagnostic => diagnostic.Code == PowerShellCompilationDiagnosticCode.CommandInvocation && diagnostic.Message.Contains("Get-Random", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_RejectsDynamicSemanticFalsePositivesBeforeGeneratedBuild()
    {
        using var fixture = CompilationFixture.Create(
            """
            function Test-Truthiness {
                param([int] $Value)
                if ($Value) { return $true }
                return $false
            }
            function Get-OverflowingAdd {
                param([int] $Left, [int] $Right)
                return $Left + $Right
            }
            function Get-BranchLocal {
                param([bool] $Flag)
                if ($Flag) { $value = 1 }
                return $value
            }
            function Get-Fallthrough {
                param([double] $Value)
                if ($Value -gt 0.0) { return $Value }
            }
            function Get-A {
                param([double] $Value)
                return $Value
            }
            function Get_A {
                param([double] $Value)
                return $Value
            }
            function Get-LoopLeak {
                param([int] $Count)
                for ([int] $value = 0; $value -lt $Count; $value++) { }
                return $value
            }
            function Convert-UnsafeUnsigned {
                param([long] $Value)
                [ulong] $result = $Value
                return $result
            }
            function Convert-TextValue {
                param([string] $Value)
                return [int] $Value
            }
            function Convert-RoundingValue {
                param([double] $Value)
                return [int] $Value
            }
            function Get-HeterogeneousValue {
                param([bool] $UseWide, [long] $Wide)
                if ($UseWide) { return $Wide }
                return 1
            }
            function Stop-LabeledLoop {
                param([bool] $KeepGoing)
                :outer while ($KeepGoing) { break outer }
                return 1
            }
            function Stop-OutsideLoop {
                break
                return 1
            }
            function Get-DynamicDivision {
                param([int] $Left, [int] $Right)
                return $Left / $Right
            }
            function Get-HeterogeneousArray {
                param([long] $Wide)
                return 1, $Wide
            }
            function Test-StringOrder {
                param([string] $Left, [string] $Right)
                return $Left -lt $Right
            }
            """);

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        Assert.Equal(16, plan.RuntimeFallbackUnits);
        Assert.All(Assert.Single(plan.Files).Units, static unit => Assert.False(unit.IsCompilable));
        var messages = string.Join(Environment.NewLine, plan.Files.SelectMany(static file => file.Units).SelectMany(static unit => unit.Diagnostics).Select(static diagnostic => diagnostic.Message));
        Assert.Contains("truthiness", messages, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("promote on overflow", messages, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("function scope", messages, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must end with an explicit return", messages, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("collides with another function", messages, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("outside the loop scope", messages, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not an implicit CLR conversion", messages, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("runtime conversion semantics", messages, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("branch-specific runtime types", messages, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Labeled break", messages, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("break must be inside", messages, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("integral division changes runtime result type", messages, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("one inferred CLR array element type", messages, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("string relational comparison", messages, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CompilationFixture : IDisposable
    {
        private CompilationFixture(string rootPath, string scriptPath)
        {
            RootPath = rootPath;
            ScriptPath = scriptPath;
        }

        public string RootPath { get; }

        public string ScriptPath { get; }

        public static CompilationFixture Create(string source)
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            var scriptPath = Path.Combine(rootPath, "input.ps1");
            File.WriteAllText(scriptPath, source);
            return new CompilationFixture(rootPath, scriptPath);
        }

        public void Dispose()
        {
            try { Directory.Delete(RootPath, recursive: true); } catch { }
        }
    }
}
