using System;
using System.IO;
using System.Linq;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed class PowerShellCompilationAnalyzerTests
{
    [Theory]
    [InlineData(PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Package)]
    [InlineData(PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Hybrid)]
    [InlineData(PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid)]
    public void PublicBuildSpecUsesArtifactKindAwareDefaultMode(
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode expectedMode)
    {
        var spec = new PowerShellCompilationBuildSpec(
            "input.ps1",
            Path.GetTempPath(),
            "DefaultMode",
            kind);

        Assert.Equal(expectedMode, spec.Mode);
        Assert.Equal(expectedMode, PowerShellCompilationBuildSpec.GetDefaultMode(kind));
    }

    [Fact]
    public void PublicCompilationSpecsRejectUndefinedModesAndKinds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PowerShellCompilationSpec(
            Path.GetTempPath(),
            (PowerShellCompilationMode)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PowerShellCompilationPlan(
            (PowerShellCompilationMode)999,
            Array.Empty<PowerShellCompilationFilePlan>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PowerShellCompilationBuildSpec(
            "input.ps1",
            Path.GetTempPath(),
            "InvalidMode",
            PowerShellCompilationArtifactKind.Executable,
            (PowerShellCompilationMode)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PowerShellCompilationBuildSpec(
            "input.ps1",
            Path.GetTempPath(),
            "InvalidKind",
            (PowerShellCompilationArtifactKind)999));
    }

    [Theory]
    [InlineData(PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Package, true)]
    [InlineData(PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid, false)]
    [InlineData(PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Strict, true)]
    [InlineData(PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Package, false)]
    [InlineData(PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Hybrid, true)]
    [InlineData(PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict, true)]
    [InlineData(PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Package, false)]
    [InlineData(PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid, true)]
    [InlineData(PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Strict, true)]
    [InlineData(PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Analyze, false)]
    public void PublicBuildSpecExposesTheArtifactModeCompatibilityMatrix(
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode,
        bool expected)
    {
        Assert.Equal(expected, PowerShellCompilationBuildSpec.IsModeSupported(kind, mode));
        if (expected)
            PowerShellCompilationBuildSpec.EnsureModeSupported(kind, mode);
        else
            Assert.Throws<ArgumentException>(() => PowerShellCompilationBuildSpec.EnsureModeSupported(kind, mode));
    }

    [Fact]
    public void Analyze_RejectsAnExplicitModeThatCannotProduceTheResolvedArtifactKind()
    {
        using var fixture = CompilationFixture.Create("param([int] $Value); return $Value");
        var resolved = new PowerShellCompilationInputResolver().Resolve(
            fixture.ScriptPath,
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package);

        var exception = Assert.Throws<ArgumentException>(() => new PowerShellCompilationAnalyzer().Analyze(
            resolved,
            PowerShellCompilationMode.Hybrid));

        Assert.Contains("Hybrid executable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

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
    public void Analyze_PrunesExcludedAndLinkedDirectoriesDuringDiscovery()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var linkedSource = Path.Combine(root, "LinkedSource");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllText(Path.Combine(root, "included.ps1"), "return 1");
            var nested = Directory.CreateDirectory(Path.Combine(root, "Source"));
            File.WriteAllText(Path.Combine(nested.FullName, "included.psm1"), "function Get-Value { return 1 }");
            var excluded = Directory.CreateDirectory(Path.Combine(root, "node_modules"));
            File.WriteAllText(Path.Combine(excluded.FullName, "excluded.ps1"), "return 2");
            File.WriteAllText(Path.Combine(outside, "linked.ps1"), "return 3");
            try
            {
                Directory.CreateSymbolicLink(linkedSource, outside);
            }
            catch (Exception exception) when (exception is PlatformNotSupportedException or UnauthorizedAccessException or IOException)
            {
                // The exclusion contract remains covered when the host cannot create directory links.
            }

            var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(root));

            Assert.Equal(2, plan.Files.Length);
            Assert.All(plan.Files, file => Assert.DoesNotContain("node_modules", file.FullPath, StringComparison.OrdinalIgnoreCase));
            Assert.All(plan.Files, file => Assert.DoesNotContain("LinkedSource", file.FullPath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(linkedSource) && (File.GetAttributes(linkedSource) & FileAttributes.ReparsePoint) != 0)
                Directory.Delete(linkedSource);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void Analyze_DoesNotIgnoreProcessBlockOrParameterDefaultSemantics()
    {
        using var fixture = CompilationFixture.Create(
            """
            function Invoke-StreamingThing {
                param([Parameter(Mandatory)] [ValidateRange(1, 10)] [int] $Value = (Get-Random))
                process {
                    $Value + 1
                }
            }
            """);

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, static diagnostic => diagnostic.Code == PowerShellCompilationDiagnosticCode.UnsupportedSyntax && diagnostic.Message.Contains("process", StringComparison.Ordinal));
        Assert.Contains(unit.Diagnostics, static diagnostic => diagnostic.Code == PowerShellCompilationDiagnosticCode.CommandInvocation && diagnostic.Message.Contains("Get-Random", StringComparison.Ordinal));
        var parameter = Assert.Single(unit.Parameters);
        Assert.True(parameter.IsMandatory);
        Assert.Equal(PowerShellCompilationValidationKind.Range, Assert.Single(parameter.Validations).Kind);
    }

    [Fact]
    public void Analyze_AcceptsConservativePowerInfoBloxHelperMetadataAndOperators()
    {
        using var fixture = CompilationFixture.Create(
            """
            function Convert-IpAddressToPtrString {
                [CmdletBinding()]
                param([Parameter(Mandatory = $true)] [string] $IPAddress)
                $octets = $IPAddress -split "\."
                [array]::Reverse($octets)
                $ptrString = ($octets -join ".") + ".in-addr.arpa"
                $ptrString
            }
            """);

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.True(unit.IsCompilable, string.Join(Environment.NewLine, unit.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.True(Assert.Single(unit.Parameters).IsMandatory);
        Assert.Equal(typeof(string).FullName, unit.ReturnType);
    }

    [Fact]
    public void Analyze_RejectsEscapingDictionaryAndDynamicParameterMetadata()
    {
        using var fixture = CompilationFixture.Create(
            """
            function Get-EscapingMap {
                return @{ Name = 'Value' }
            }
            function Get-DynamicMetadata {
                param([Parameter(ValueFromPipeline = $true)] [string] $Value)
                return $Value
            }
            """);

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        Assert.Equal(2, plan.RuntimeFallbackUnits);
        var units = Assert.Single(plan.Files).Units;
        Assert.Contains(units.Single(unit => unit.Name == "Get-EscapingMap").Diagnostics, diagnostic =>
            diagnostic.Message.Contains("lookup-only local", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(units.Single(unit => unit.Name == "Get-DynamicMetadata").Diagnostics, diagnostic =>
            diagnostic.Message.Contains("AttributeAst", StringComparison.Ordinal));
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
            function Test-StringOrder {
                param([string] $Left, [string] $Right)
                return $Left -lt $Right
            }
            function Test-NullScalar {
                param([int] $Value)
                return $Value -eq $null
            }
            function Test-BooleanOrder {
                param([bool] $Left, [bool] $Right)
                return $Left -lt $Right
            }
            filter Get-FilteredValue {
                return 1
            }
            """);

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        Assert.Equal(18, plan.RuntimeFallbackUnits);
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
        Assert.Contains("string relational comparison", messages, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("non-nullable CLR value", messages, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Relational comparison for CLR type 'System.Boolean'", messages, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("per-pipeline-input", messages, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_ExcludesConditionallyDeclaredFunctionsFromTypedUnits()
    {
        using var fixture = CompilationFixture.Create(
            "if ($false) { function Get-ConditionalValue { return 1 } }; function Get-TopValue { return 2 }");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath, PowerShellCompilationMode.Hybrid));

        var units = Assert.Single(plan.Files).Units;
        Assert.DoesNotContain(units, static unit => unit.Name == "Get-ConditionalValue");
        Assert.Contains(units, static unit => unit.Name == "Get-TopValue" && unit.IsCompilable);
    }

    [Fact]
    public void Analyze_RoutesRuntimeBearingUsingModuleToWholeFileFallback()
    {
        using var fixture = CompilationFixture.Create("using module Microsoft.PowerShell.Utility\nfunction Get-TypedValue { return 1 }");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, diagnostic =>
            diagnostic.Code == PowerShellCompilationDiagnosticCode.UnsupportedSyntax &&
            diagnostic.Message.Contains("runtime-bearing using", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RoutesRuntimeBearingUsingAssemblyToWholeFileFallback()
    {
        using var fixture = CompilationFixture.Create("using assembly './runtime.dll'\nfunction Get-TypedValue { return 1 }");
        File.Copy(typeof(object).Assembly.Location, Path.Combine(fixture.RootPath, "runtime.dll"));

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, diagnostic =>
            diagnostic.Code == PowerShellCompilationDiagnosticCode.UnsupportedSyntax &&
            diagnostic.Message.Contains("runtime-bearing using", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RoutesVoidInvocationAssignmentToFallbackBeforeGeneratedBuild()
    {
        using var fixture = CompilationFixture.Create(
            "function Get-Value { $ignored = [Console]::WriteLine('text'); return 1 }");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("void CLR invocation", StringComparison.OrdinalIgnoreCase));
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
