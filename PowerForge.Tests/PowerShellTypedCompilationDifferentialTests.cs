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

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellTypedCompilationDifferentialTests
{
    [Fact]
    public void TypedFloatingArithmeticPreservesDivisionAndRemainderZeroSemantics()
    {
        const string source =
            "function Divide-Single { param([float] $Left, [float] $Right); return $Left / $Right }; " +
            "function Divide-Double { param([double] $Left, [double] $Right); return $Left / $Right }; " +
            "function Remainder-Single { param([float] $Left, [float] $Right); return $Left % $Right }; " +
            "function Remainder-Double { param([double] $Left, [double] $Right); return $Left % $Right }";
        using var fixture = DifferentialFixture.Create(source);
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.SingleDivisionDifferential",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);

        using var assemblyStream = File.OpenRead(build.ArtifactPath!);
        var loadContext = new AssemblyLoadContext("PowerForgeSingleDivisionDifferential", isCollectible: true);
        using var runspace = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault2());
        runspace.Open();
        InitializePowerShellSource(runspace, source);
        try
        {
            var generatedType = loadContext.LoadFromStream(assemblyStream)
                .GetType("PowerForge.Compiled.PowerForge_SingleDivisionDifferentialMethods", throwOnError: true)!;
            AssertDifferential(generatedType, runspace, "Divide-Single", "Divide_Single", new[]
            {
                new object[] { 1f, 3f },
                new object[] { 16_777_215f, 3f },
                new object[] { 1f, 0f },
                new object[] { 0f, 0f }
            });
            AssertDifferential(generatedType, runspace, "Divide-Double", "Divide_Double", new[]
            {
                new object[] { 1d, 3d },
                new object[] { -10d, 4d },
                new object[] { -1d, 0d },
                new object[] { 0d, 0d }
            });
            AssertDifferential(generatedType, runspace, "Remainder-Single", "Remainder_Single", new[]
            {
                new object[] { 10f, 3f },
                new object[] { -10f, 3f },
                new object[] { 1f, 0f },
                new object[] { 0f, 0f }
            });
            AssertDifferential(generatedType, runspace, "Remainder-Double", "Remainder_Double", new[]
            {
                new object[] { 10d, 3d },
                new object[] { -10d, 3d },
                new object[] { -1d, 0d },
                new object[] { 0d, 0d }
            });
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Fact]
    public void TypedCommonModuleHelpersMatchPowerShell()
    {
        const string source =
            """
            function Convert-IpAddressToPtrString {
                [CmdletBinding()]
                param([Parameter(Mandatory = $true)] [string] $IPAddress)
                $octets = $IPAddress -split "\."
                [array]::Reverse($octets)
                $ptrString = ($octets -join ".") + ".in-addr.arpa"
                $ptrString
            }
            """;
        using var fixture = DifferentialFixture.Create(source);
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.CommonModuleHelpers",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);

        using var assemblyStream = File.OpenRead(build.ArtifactPath!);
        var loadContext = new AssemblyLoadContext("PowerForgeCommonModuleHelpers", isCollectible: true);
        using var runspace = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault2());
        runspace.Open();
        InitializePowerShellSource(runspace, source);
        try
        {
            var generatedType = loadContext.LoadFromStream(assemblyStream)
                .GetType("PowerForge.Compiled.PowerForge_CommonModuleHelpersMethods", throwOnError: true)!;
            AssertDifferential(generatedType, runspace, "Convert-IpAddressToPtrString", "Convert_IpAddressToPtrString", new[]
            {
                new object[] { "192.168.1.20" }
            });
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Fact]
    public void StrictCompilationRejectsNullableClrReceiverWithoutExactPowerShellErrorIdentity()
    {
        const string source =
            "function Test-MissingEnvironmentPrefix { param([string] $Name); $value = [Environment]::GetEnvironmentVariable($Name); return $value.StartsWith('PowerForge') }; " +
            "function Get-MissingEnvironmentLength { param([string] $Name); $value = [Environment]::GetEnvironmentVariable($Name); return $value.Length }";
        using var fixture = DifferentialFixture.Create(source);
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NullableMemberDifferential",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));
        Assert.False(build.Succeeded);
        Assert.Contains("potentially null receiver", build.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void TypedNullableReferencePropertyAccessPreservesPowerShellMissingValues()
    {
        const string source =
            "function Get-ResolvedTypeName { param([string] $Name); return [Type]::GetType($Name).Name }";
        using var fixture = DifferentialFixture.Create(source);
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NullablePropertyDifferential",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);

        using var assemblyStream = File.OpenRead(build.ArtifactPath!);
        var loadContext = new AssemblyLoadContext("PowerForgeNullablePropertyDifferential", isCollectible: true);
        using var runspace = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault2());
        runspace.Open();
        InitializePowerShellSource(runspace, source);
        try
        {
            var generatedType = loadContext.LoadFromStream(assemblyStream)
                .GetType("PowerForge.Compiled.PowerForge_NullablePropertyDifferentialMethods", throwOnError: true)!;
            AssertDifferential(generatedType, runspace, "Get-ResolvedTypeName", "Get_ResolvedTypeName", new[]
            {
                new object[] { "System.String" },
                new object[] { "PowerForge.Missing." + Guid.NewGuid().ToString("N") }
            });
        }
        finally
        {
            loadContext.Unload();
        }

        const string valueSource =
            "function Get-ResolvedTypeToken { param([string] $Name); return [Type]::GetType($Name).MetadataToken }";
        using var valueFixture = DifferentialFixture.Create(valueSource);
        var valueBuild = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            valueFixture.ScriptPath,
            valueFixture.OutputPath,
            "PowerForge.NullableValuePropertyDifferential",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));
        Assert.True(valueBuild.Succeeded, valueBuild.Error + Environment.NewLine + valueBuild.BuildOutput);

        using var valueAssemblyStream = File.OpenRead(valueBuild.ArtifactPath!);
        var valueLoadContext = new AssemblyLoadContext("PowerForgeNullableValuePropertyDifferential", isCollectible: true);
        using var valueRunspace = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault2());
        valueRunspace.Open();
        InitializePowerShellSource(valueRunspace, valueSource);
        try
        {
            var generatedType = valueLoadContext.LoadFromStream(valueAssemblyStream)
                .GetType("PowerForge.Compiled.PowerForge_NullableValuePropertyDifferentialMethods", throwOnError: true)!;
            AssertDifferential(generatedType, valueRunspace, "Get-ResolvedTypeToken", "Get_ResolvedTypeToken", new[]
            {
                new object[] { "System.String" },
                new object[] { "PowerForge.Missing." + Guid.NewGuid().ToString("N") }
            });
        }
        finally
        {
            valueLoadContext.Unload();
        }
    }

    [Fact]
    public void TypedVoidClrReturnLowersToInvocationThenReturn()
    {
        const string source = "function Write-TypedLine { param([string] $Value); return ([Console]::WriteLine($Value)) }";
        using var fixture = DifferentialFixture.Create(source);
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.VoidMemberDifferential",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
    }

    [Fact]
    public void TypedTerminalTryExpressionOutputMatchesPowerShell()
    {
        const string source =
            "function Test-TerminalTryOutput { param([int] $Value) try { $result = $Value -gt 0; $result } catch { return $false } }";
        using var fixture = DifferentialFixture.Create(source);
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TerminalTryOutputDifferential",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);

        using var assemblyStream = File.OpenRead(build.ArtifactPath!);
        var loadContext = new AssemblyLoadContext("PowerForgeTerminalTryOutputDifferential", isCollectible: true);
        using var runspace = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault2());
        runspace.Open();
        InitializePowerShellSource(runspace, source);
        try
        {
            var generatedType = loadContext.LoadFromStream(assemblyStream)
                .GetType("PowerForge.Compiled.PowerForge_TerminalTryOutputDifferentialMethods", throwOnError: true)!;
            AssertDifferential(generatedType, runspace, "Test-TerminalTryOutput", "Test_TerminalTryOutput", new[]
            {
                new object[] { -1 },
                new object[] { 1 }
            });
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Fact]
    public void TypedMethodsMatchPowerShellForStaticallyResolvedClrMembers()
    {
        const string source =
            """
            function Test-TextPrefix {
                param([string] $Value, [string] $Prefix)
                return $Value.StartsWith($Prefix, [System.StringComparison]::OrdinalIgnoreCase)
            }

            function Test-ParenthesizedTextPrefix {
                param([string] $Value, [string] $Prefix)
                return ($Value).StartsWith($Prefix, [System.StringComparison]::OrdinalIgnoreCase)
            }

            function Get-TextLength {
                param([string] $Value)
                return $Value.Length
            }

            function Test-NullText {
                param([string] $Value)
                return $Value -eq $null
            }

            function Get-BoundText {
                param([string] $Value)
                return $Value
            }

            function Get-LeafName {
                param([string] $Value)
                return [System.IO.Path]::GetFileName($Value)
            }

            function Get-TimeSpanSeconds {
                param([int] $Seconds)
                $value = [System.TimeSpan]::new(0, 0, $Seconds)
                return $value.TotalSeconds
            }

            function Get-ArrayLength {
                param([int[]] $Values)
                return $Values.Length
            }

            function Get-IndexedValue {
                param([int[]] $Values, [int] $Index)
                return $Values[$Index]
            }

            function Get-IndexedCharacter {
                param([string] $Value, [int] $Index)
                return $Value[$Index]
            }

            function Get-LastIndexedValue {
                param([int[]] $Values)
                return $Values[-1]
            }

            function Get-LastIndexedCharacter {
                param([string] $Value)
                return $Value[-1]
            }
            """;
        using var fixture = DifferentialFixture.Create(source);
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath, PowerShellCompilationMode.Strict));
        Assert.True(
            plan.CanProceed,
            string.Join(Environment.NewLine, plan.Files.SelectMany(file => file.Diagnostics.Concat(file.Units.SelectMany(unit => unit.Diagnostics))).Select(diagnostic => diagnostic.Message)));
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.MemberDifferential",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);

        using var assemblyStream = File.OpenRead(build.ArtifactPath!);
        var loadContext = new AssemblyLoadContext("PowerForgeMemberDifferential", isCollectible: true);
        using var runspace = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault2());
        runspace.Open();
        InitializePowerShellSource(runspace, source);
        try
        {
            var type = loadContext.LoadFromStream(assemblyStream)
                .GetType("PowerForge.Compiled.PowerForge_MemberDifferentialMethods", throwOnError: true)!;
            AssertDifferential(type, runspace, "Test-TextPrefix", "Test_TextPrefix", new[]
            {
                new object[] { "PowerForge", "power" },
                new object[] { "PowerForge", "forge" },
                new object[] { string.Empty, string.Empty },
                new object[] { null!, string.Empty }
            });
            AssertDifferential(type, runspace, "Test-ParenthesizedTextPrefix", "Test_ParenthesizedTextPrefix", new[]
            {
                new object[] { "PowerForge", "power" },
                new object[] { string.Empty, string.Empty },
                new object[] { null!, string.Empty }
            });
            AssertDifferential(type, runspace, "Get-TextLength", "Get_TextLength", new[]
            {
                new object[] { "PowerForge" }, new object[] { string.Empty }, new object[] { null! }
            });
            AssertDifferential(type, runspace, "Test-NullText", "Test_NullText", new[]
            {
                new object[] { "PowerForge" }, new object[] { string.Empty }, new object[] { null! }
            });
            AssertDifferential(type, runspace, "Get-BoundText", "Get_BoundText", new[]
            {
                new object[] { "PowerForge" }, new object[] { string.Empty }, new object[] { null! }
            });
            AssertDifferential(type, runspace, "Get-LeafName", "Get_LeafName", new[]
            {
                new object[] { "C:/Support/PowerForge.ps1" }, new object[] { "PowerForge.ps1" }
            });
            AssertDifferential(type, runspace, "Get-TimeSpanSeconds", "Get_TimeSpanSeconds", new[]
            {
                new object[] { 0 }, new object[] { 59 }, new object[] { 90 }, new object[] { -10 }
            });
            AssertDifferential(type, runspace, "Get-ArrayLength", "Get_ArrayLength", new[]
            {
                new object[] { null! }, new object[] { Array.Empty<int>() }, new object[] { new[] { 1, 2, 3 } }
            });
            AssertDifferential(type, runspace, "Get-IndexedValue", "Get_IndexedValue", new[]
            {
                new object[] { new[] { 10, 20, 30 }, 0 }, new object[] { new[] { 10, 20, 30 }, 2 }, new object[] { new[] { 10, 20, 30 }, -1 },
                new object[] { new[] { 10, 20, 30 }, 5 }, new object[] { new[] { 10, 20, 30 }, -5 }, new object[] { Array.Empty<int>(), 0 }
            });
            AssertDifferential(type, runspace, "Get-IndexedCharacter", "Get_IndexedCharacter", new[]
            {
                new object[] { "PowerForge", 0 }, new object[] { "PowerForge", 5 }, new object[] { "PowerForge", -1 },
                new object[] { "PowerForge", 99 }, new object[] { "PowerForge", -99 }, new object[] { string.Empty, 0 }, new object[] { null!, 0 }
            });
            AssertDifferential(type, runspace, "Get-LastIndexedValue", "Get_LastIndexedValue", new[]
            {
                new object[] { new[] { 10, 20, 30 } }, new object[] { Array.Empty<int>() }
            });
            AssertDifferential(type, runspace, "Get-LastIndexedCharacter", "Get_LastIndexedCharacter", new[]
            {
                new object[] { "PowerForge" }, new object[] { string.Empty }, new object[] { null! }
            });
            AssertIndexingNullArrayFails(type, runspace);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Fact]
    public void TypedInlineStringAssignmentPreservesConstrainedNullNormalization()
    {
        const string source =
            "function Get-InlineEnvironmentValue { param([string] $Name); [string] $value = 'seed'; if ((($value = [Environment]::GetEnvironmentVariable($Name)) -eq '')) { }; return $value }";
        using var fixture = DifferentialFixture.Create(source);
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.InlineStringDifferential",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);

        using var assemblyStream = File.OpenRead(build.ArtifactPath!);
        var loadContext = new AssemblyLoadContext("PowerForgeInlineStringDifferential", isCollectible: true);
        using var runspace = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault2());
        runspace.Open();
        InitializePowerShellSource(runspace, source);
        var missingName = "POWERFORGE_MISSING_" + Guid.NewGuid().ToString("N");
        try
        {
            var generatedType = loadContext.LoadFromStream(assemblyStream)
                .GetType("PowerForge.Compiled.PowerForge_InlineStringDifferentialMethods", throwOnError: true)!;
            AssertDifferential(generatedType, runspace, "Get-InlineEnvironmentValue", "Get_InlineEnvironmentValue", new[]
            {
                new object[] { missingName }
            });
        }
        finally
        {
            loadContext.Unload();
        }
    }

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

            function Test-DoubleSelfEquality {
                param([double] $Value)
                return $Value -eq $Value
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
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true);
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
                new object[] { 5d, 0d, 10d }, new object[] { -1d, 0d, 10d }, new object[] { 10d, 0d, 10d },
                new object[] { double.NaN, 0d, 10d }
            });
            AssertDifferential(type, runspace, "Test-DoubleSelfEquality", "Test_DoubleSelfEquality", new[]
            {
                new object[] { double.NaN }, new object[] { 0d }, new object[] { double.PositiveInfinity }
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
            var powerShellValue = output.Count == 0 ? null : Assert.Single(output)?.BaseObject;
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
        if (expected is char || actual is char)
        {
            Assert.Equal(expected, actual);
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

    private static void AssertIndexingNullArrayFails(Type generatedType, Runspace runspace)
    {
        using var powerShell = PowerShell.Create();
        powerShell.Runspace = runspace;
        powerShell.AddCommand("Get-IndexedValue").AddArgument(null).AddArgument(0);
        powerShell.Invoke();
        Assert.True(powerShell.HadErrors);

        var method = generatedType.GetMethod("Get_IndexedValue", BindingFlags.Public | BindingFlags.Static)!;
        var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object?[] { null, 0 }));
        Assert.IsType<InvalidOperationException>(exception.InnerException);
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
