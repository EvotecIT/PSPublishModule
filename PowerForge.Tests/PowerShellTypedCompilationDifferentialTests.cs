using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Runtime.Loader;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed class PowerShellTypedCompilationDifferentialTests
{
    [Fact]
    public void TypedMethodsMatchPowerShellAcrossArithmeticLoopDivisionAndStringBranches()
    {
        const string source =
            """
            function Get-AllowedAverageMs {
                param([double] $BaselineMs, [double] $RelativeTolerance, [double] $AbsoluteToleranceMs)
                $relativeCap = $BaselineMs * (1.0 + $RelativeTolerance)
                $absoluteCap = $BaselineMs + $AbsoluteToleranceMs
                if ($relativeCap -gt $absoluteCap) { return $relativeCap }
                return $absoluteCap
            }

            function Get-TriangularNumber {
                param([int] $Count)
                [long] $total = 0
                for ([int] $i = 1; $i -le $Count; $i++) { $total += $i }
                return $total
            }

            function Test-Label {
                param([string] $Left, [string] $Right)
                if ($Left -eq $Right) { return $true }
                return $false
            }

            function Get-CasedValue {
                param([double] $Value)
                return $value
            }

            function Get-WhileTotal {
                param([int] $Count)
                [long] $total = 0
                [int] $value = 1
                while ($value -le $Count) {
                    $total += $value
                    $value += 1
                }
                return $total
            }

            function Get-ArrayTotal {
                param([int[]] $Values)
                [long] $total = 0
                foreach ($value in $Values) {
                    $total += $value
                }
                return $total
            }

            function Get-BoundedTotal {
                param([int] $Count)
                [long] $total = 0
                for ([int] $value = 0; $value -lt $Count; $value++) {
                    if ($value -eq 2) { continue }
                    if ($value -ge 5) { break }
                    $total += $value
                }
                return $total
            }

            function Test-ExactLabel {
                param([string] $Left, [string] $Right)
                if ($Left -ceq $Right) { return $true }
                return $false
            }

            function Add-DecimalValue {
                param([decimal] $Left, [decimal] $Right)
                return $Left + $Right
            }

            function Test-InRange {
                param([double] $Value, [double] $Minimum, [double] $Maximum)
                if (($Value -ge $Minimum) -and ($Value -le $Maximum)) { return $true }
                return $false
            }
            """;
        using var fixture = DifferentialFixture.Create(source);
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath, PowerShellCompilationMode.Strict));
        Assert.True(
            plan.CanProceed,
            string.Join(Environment.NewLine, plan.Files.SelectMany(file => file.Diagnostics.Concat(file.Units.SelectMany(unit => unit.Diagnostics))).Select(diagnostic => diagnostic.Message)));
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.Differential",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict);
        var build = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);

        using var assemblyStream = File.OpenRead(build.ArtifactPath!);
        var loadContext = new AssemblyLoadContext("PowerForgeDifferential", isCollectible: true);
        using var runspace = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault2());
        runspace.Open();
        InitializePowerShellSource(runspace, source);
        try
        {
            var type = loadContext.LoadFromStream(assemblyStream)
                .GetType("PowerForge.Compiled.PowerForge_DifferentialMethods", throwOnError: true)!;

            AssertDifferential(type, runspace, "Get-AllowedAverageMs", "Get_AllowedAverageMs", new[]
            {
                new object[] { 100d, 0.2d, 30d },
                new object[] { 100d, 0.5d, 30d },
                new object[] { -100d, 0.2d, 15d },
                new object[] { 0d, 0d, 0d }
            });
            AssertDifferential(type, runspace, "Get-TriangularNumber", "Get_TriangularNumber", new[]
            {
                new object[] { -5 }, new object[] { 0 }, new object[] { 1 }, new object[] { 10 }, new object[] { 10_000 }
            });
            AssertDifferential(type, runspace, "Test-Label", "Test_Label", new[]
            {
                new object[] { "PowerForge", "powerforge" },
                new object[] { "PowerForge", "Power Forge" },
                new object[] { string.Empty, string.Empty }
            });
            AssertDifferential(type, runspace, "Get-CasedValue", "Get_CasedValue", new[]
            {
                new object[] { -12.5d }, new object[] { 0d }, new object[] { 42.75d }
            });
            AssertDifferential(type, runspace, "Get-WhileTotal", "Get_WhileTotal", new[]
            {
                new object[] { -1 }, new object[] { 0 }, new object[] { 1 }, new object[] { 100 }
            });
            AssertDifferential(type, runspace, "Get-ArrayTotal", "Get_ArrayTotal", new[]
            {
                new object[] { null! }, new object[] { Array.Empty<int>() }, new object[] { new[] { 1 } }, new object[] { new[] { -2, 3, 10 } }
            });
            AssertDifferential(type, runspace, "Get-BoundedTotal", "Get_BoundedTotal", new[]
            {
                new object[] { 0 }, new object[] { 3 }, new object[] { 5 }, new object[] { 100 }
            });
            AssertDifferential(type, runspace, "Test-ExactLabel", "Test_ExactLabel", new[]
            {
                new object[] { "PowerForge", "PowerForge" }, new object[] { "PowerForge", "powerforge" }
            });
            AssertDifferential(type, runspace, "Add-DecimalValue", "Add_DecimalValue", new[]
            {
                new object[] { 1.25m, 2.75m }, new object[] { -10m, 0.5m }, new object[] { decimal.MaxValue, 0m }
            });
            AssertDifferential(type, runspace, "Test-InRange", "Test_InRange", new[]
            {
                new object[] { 5d, 0d, 10d }, new object[] { -1d, 0d, 10d }, new object[] { 10d, 0d, 10d }
            });
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static void InitializePowerShellSource(Runspace runspace, string source)
    {
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddScript(source, useLocalScope: false);
        powerShell.Invoke();
        Assert.False(powerShell.HadErrors, string.Join(Environment.NewLine, powerShell.Streams.Error));
    }

    private static void AssertDifferential(
        Type generatedType,
        Runspace runspace,
        string powerShellName,
        string generatedName,
        IEnumerable<object[]> cases)
    {
        var method = generatedType.GetMethod(generatedName, BindingFlags.Public | BindingFlags.Static)!;
        foreach (var arguments in cases)
        {
            using var powerShell = PowerShell.Create();
            powerShell.Runspace = runspace;
            powerShell.AddCommand(powerShellName);
            foreach (var argument in arguments) powerShell.AddArgument(argument);
            var output = powerShell.Invoke();
            Assert.False(powerShell.HadErrors, string.Join(Environment.NewLine, powerShell.Streams.Error));
            var powerShellValue = Assert.Single(output).BaseObject;
            var compiledValue = method.Invoke(null, arguments);
            AssertEquivalent(powerShellValue, compiledValue, $"{powerShellName}({string.Join(", ", arguments)})");
        }
    }

    private static void AssertEquivalent(object? expected, object? actual, string caseName)
    {
        Assert.True(
            expected?.GetType() == actual?.GetType(),
            $"{caseName}: PowerShell returned type '{expected?.GetType().FullName ?? "<null>"}', compiled CLR returned '{actual?.GetType().FullName ?? "<null>"}'.");
        if (expected is double || actual is double)
        {
            Assert.Equal(
                Convert.ToDouble(expected, CultureInfo.InvariantCulture),
                Convert.ToDouble(actual, CultureInfo.InvariantCulture),
                precision: 12);
            return;
        }
        if (expected is IConvertible && actual is IConvertible && expected is not string && actual is not string && expected is not bool && actual is not bool)
        {
            Assert.Equal(
                Convert.ToDecimal(expected, CultureInfo.InvariantCulture),
                Convert.ToDecimal(actual, CultureInfo.InvariantCulture));
            return;
        }
        Assert.True(Equals(expected, actual), $"{caseName}: PowerShell returned '{expected}' ({expected?.GetType().FullName}); compiled CLR returned '{actual}' ({actual?.GetType().FullName}).");
    }

    private sealed class DifferentialFixture : IDisposable
    {
        private DifferentialFixture(string rootPath, string scriptPath, string outputPath)
        {
            RootPath = rootPath;
            ScriptPath = scriptPath;
            OutputPath = outputPath;
        }

        public string RootPath { get; }
        public string ScriptPath { get; }
        public string OutputPath { get; }

        public static DifferentialFixture Create(string source)
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
            var outputPath = Path.Combine(rootPath, "output");
            Directory.CreateDirectory(outputPath);
            var scriptPath = Path.Combine(rootPath, "input.ps1");
            File.WriteAllText(scriptPath, source);
            return new DifferentialFixture(rootPath, scriptPath, outputPath);
        }

        public void Dispose()
        {
            try { Directory.Delete(RootPath, recursive: true); } catch { }
        }
    }
}
