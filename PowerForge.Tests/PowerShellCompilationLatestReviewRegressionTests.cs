using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void Build_PackagedExecutablePreservesPathVariablesDuringParameterBinding()
    {
        using var fixture = ArtifactFixture.Create(
            "param([string] $Config = \"$PSScriptRoot/config.json\", [string] $Command = $PSCommandPath, " +
            "[ValidateScript({ (Test-Path \"$PSScriptRoot/config.json\") -and $PSCommandPath -eq [System.Environment]::ProcessPath })] [string] $Value); " +
            "$Config; $Command; $Value");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.PackagedDefaultPaths",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(result.ArtifactPath!)!, "config.json"), "{}");
        var run = Run(result.ArtifactPath!, "-Value", "accepted");
        Assert.Equal(0, run.ExitCode);
        var output = run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, output.Length);
        AssertPathsEqual(Path.Combine(Path.GetDirectoryName(result.ArtifactPath!)!, "config.json"), output[0]);
        AssertPathsEqual(result.ArtifactPath!, output[1]);
        Assert.Equal("accepted", output[2]);
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void TargetFrameworkAnalysisRequiresAHostAtLeastAsNewAsTheModernTarget()
    {
        Assert.True(PowerShellGeneratedTargetFrameworkPolicy.IsHostCompatible(null, 4, isNetFrameworkHost: true));
        Assert.True(PowerShellGeneratedTargetFrameworkPolicy.IsHostCompatible("net472", 4, isNetFrameworkHost: true));
        Assert.False(PowerShellGeneratedTargetFrameworkPolicy.IsHostCompatible("net8.0", 4, isNetFrameworkHost: true));
        Assert.True(PowerShellGeneratedTargetFrameworkPolicy.IsHostCompatible("net8.0", 8, isNetFrameworkHost: false));
        Assert.False(PowerShellGeneratedTargetFrameworkPolicy.IsHostCompatible("net10.0", 8, isNetFrameworkHost: false));
        Assert.True(PowerShellGeneratedTargetFrameworkPolicy.IsHostCompatible("net10.0", 10, isNetFrameworkHost: false));
    }

    [Fact]
    public void Build_RejectsLinkedOutputAncestorBeforeReplacingProtectedSource()
    {
        var container = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var physicalRoot = Path.Combine(container, "physical-root");
        var physicalOutput = Path.Combine(physicalRoot, "nested-output");
        var linkedRoot = Path.Combine(container, "linked-root");
        var linkedOutput = Path.Combine(linkedRoot, "nested-output");
        const string artifactName = "PowerForge.LinkedOutput";
        var protectedDirectory = Path.Combine(physicalOutput, artifactName);
        var sourcePath = Path.Combine(protectedDirectory, "input.ps1");
        Directory.CreateDirectory(protectedDirectory);
        File.WriteAllText(sourcePath, "function Get-Value { return 1 }");
        try
        {
            Directory.CreateSymbolicLink(linkedRoot, physicalRoot);
        }
        catch (UnauthorizedAccessException)
        {
            Directory.Delete(container, recursive: true);
            return;
        }
        catch (PlatformNotSupportedException)
        {
            Directory.Delete(container, recursive: true);
            return;
        }

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                    sourcePath,
                    linkedOutput,
                    artifactName,
                    PowerShellCompilationArtifactKind.Library,
                    PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)));

            Assert.Contains("symbolic link or junction", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(sourcePath));
            Assert.Equal("function Get-Value { return 1 }", File.ReadAllText(sourcePath));
        }
        finally
        {
            try { Directory.Delete(linkedRoot); } catch { }
            try { Directory.Delete(container, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Build_PackagedExecutableRejectsDotSourcedDependencyBeforePublishing()
    {
        using var fixture = ArtifactFixture.Create(". $PSScriptRoot/Helper.ps1; Get-HelperValue");
        File.WriteAllText(Path.Combine(fixture.RootPath, "Helper.ps1"), "function Get-HelperValue { return 9 }");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.PackagedDotSource",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package, allowUnreviewedDependencyResolution: true));

        Assert.False(result.Succeeded);
        Assert.Contains("dot-sourced", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_PackagedExecutableRejectsExplicitNamedBlocksBeforePublishing()
    {
        var sources = new[]
        {
            "dynamicparam { } end { 'done' }",
            "begin { 'begin' } process { 'process' } end { 'end' }",
            "clean { 'clean' } end { 'end' }"
        };
        foreach (var source in sources)
        {
            using var fixture = ArtifactFixture.Create(source);
            var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                fixture.ScriptPath,
                fixture.OutputPath,
                "PowerForge.PackagedNamedBlock",
                PowerShellCompilationArtifactKind.Executable,
                PowerShellCompilationMode.Package, allowUnreviewedDependencyResolution: true));

            Assert.False(result.Succeeded);
            Assert.Contains("named block", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
        }
    }

    [Fact]
    public void Build_PackagedExecutableRejectsInteractiveHostRequirements()
    {
        var sources = new[]
        {
            "Read-Host -Prompt 'Name'",
            "$Host.UI.PromptForChoice('Title', 'Question', @(), 0)",
            "Get-Credential"
        };
        foreach (var source in sources)
        {
            using var fixture = ArtifactFixture.Create(source);
            var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                fixture.ScriptPath,
                fixture.OutputPath,
                "PowerForge.PackagedInteractiveHost",
                PowerShellCompilationArtifactKind.Executable,
                PowerShellCompilationMode.Package, allowUnreviewedDependencyResolution: true));

            Assert.False(result.Succeeded);
            Assert.Contains("interactive", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
        }
    }

    [Fact]
    public void Analyze_AllowsTypedArrayMutationAndRejectsStaticMemberMutation()
    {
        using var fixture = ArtifactFixture.Create(
            "function Set-Indexed { param([int[]] $Values) $Values[0] = 9; return $Values[0] } " +
            "function Set-Member { [System.Environment]::ExitCode = 1; return 1 }");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        var units = Assert.Single(plan.Files).Units;
        Assert.Equal(2, units.Length);
        Assert.True(units.Single(unit => unit.Name == "Set-Indexed").IsCompilable);
        var unsafeMember = units.Single(unit => unit.Name == "Set-Member");
        Assert.False(unsafeMember.IsCompilable);
        Assert.Contains(unsafeMember.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("direct local-variable assignment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RejectsWritesToReadOnlyAutomaticVariables()
    {
        using var fixture = ArtifactFixture.Create(
            "function Set-True { $true = $false; return 1 } " +
            "function Set-Home { $HOME = 'elsewhere'; return 1 } " +
            "function Set-Pid { $PID = 1; return 1 } " +
            "function Set-Edition { $PSEdition = 'Desktop'; return 1 }");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        var units = Assert.Single(plan.Files).Units;
        Assert.Equal(4, units.Length);
        Assert.All(units, static unit =>
        {
            Assert.False(unit.IsCompilable);
            Assert.Contains(unit.Diagnostics, diagnostic =>
                diagnostic.Message.Contains("read-only automatic variable", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void Analyze_RejectsConditionallyUnassignedValueTypeLocals()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-ConditionalBoolean { if ($value = $true) { }; return $value } " +
            "function Get-ConditionalInteger { if ($value = 9) { }; return $value }");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));

        var units = Assert.Single(plan.Files).Units;
        Assert.Equal(2, units.Length);
        Assert.All(units, static unit =>
        {
            Assert.False(unit.IsCompilable);
            Assert.Contains(unit.Diagnostics, diagnostic =>
                diagnostic.Message.Contains("may remain unassigned", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void BuildSpec_DefaultModesAreValidForEveryArtifactKind()
    {
        using var fixture = ArtifactFixture.Create("function Get-Value { return 1 }");
        var library = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DefaultLibrary",
            PowerShellCompilationArtifactKind.Library, allowUnreviewedDependencyResolution: true);
        var module = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DefaultModule",
            PowerShellCompilationArtifactKind.BinaryModule, allowUnreviewedDependencyResolution: true);
        var executable = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DefaultExecutable",
            PowerShellCompilationArtifactKind.Executable, allowUnreviewedDependencyResolution: true);

        Assert.Equal(PowerShellCompilationMode.Hybrid, library.Mode);
        Assert.Equal(PowerShellCompilationMode.Hybrid, module.Mode);
        Assert.Equal(PowerShellCompilationMode.Package, executable.Mode);
        var result = new PowerShellCompilationArtifactBuilder().Build(library);
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
    }

    [Fact]
    public void Analyze_PackageModeDoesNotApplyTypedTargetFrameworkSurface()
    {
        using var fixture = ArtifactFixture.Create("return [System.DateOnly]::MinValue");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Package,
            targetFramework: "net472"));

        Assert.Equal("net472", plan.TargetFramework);
        Assert.Equal(1, plan.TotalUnits);
        Assert.Equal(1, plan.CompilableUnits);
    }

    [Fact]
    public void Build_HybridLibraryRoutesNormalizedMethodNameCollisionsToDiagnostics()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-A-B { return 1 }; function Get-A_B { return 2 }; function Get-SafeValue { return 3 }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NormalizedCollision",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.Equal(2, result.Manifest.OmittedUnits);
        Assert.Equal(2, result.Manifest.Diagnostics.Count(diagnostic =>
            diagnostic.Message.Contains("collides with another function", StringComparison.OrdinalIgnoreCase)));
        var assembly = System.Reflection.Assembly.LoadFile(result.ArtifactPath!);
        var type = assembly.GetType("PowerForge.Compiled.PowerForge_NormalizedCollisionMethods", throwOnError: true)!;
        Assert.Null(type.GetMethod("Get_A_B"));
        Assert.Equal(3, type.GetMethod("Get_SafeValue")!.Invoke(null, null));
    }

    [Fact]
    public void Build_PackagedExecutableRecognizesUniqueSwitchAbbreviationBeforeConsumingPositionals()
    {
        using var fixture = ArtifactFixture.Create(
            "param([switch] $Force, [string] $Name); return \"$($Force.IsPresent)|$Name\"");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.PackagedAbbreviation",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = Run(result.ArtifactPath!, "-Fo", "Ada");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("True|Ada", run.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_StrictLibraryEscapesControlAndUnicodeLineSeparatorConstants()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-ControlText { return \"`0" + "\u0085\u2028\u2029" + "\" }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ControlConstants",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var assembly = System.Reflection.Assembly.LoadFile(result.ArtifactPath!);
        var type = assembly.GetType("PowerForge.Compiled.PowerForge_ControlConstantsMethods", throwOnError: true)!;
        Assert.Equal("\0\u0085\u2028\u2029", type.GetMethod("Get_ControlText")!.Invoke(null, null));
    }

    [Fact]
    public void Build_StrictExecutableEscapesUnicodeLineSeparatorsInParameterNames()
    {
        var parameterName = "Value\u2028Part";
        using var fixture = ArtifactFixture.Create(
            "param([string] ${" + parameterName + "}); return ${" + parameterName + "}");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ParameterLiteral",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = Run(result.ArtifactPath!, "--" + parameterName, "accepted");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("accepted", run.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_RejectsArtifactNamesInThePublicationControlNamespace()
    {
        using var fixture = ArtifactFixture.Create("function Get-Value { return 1 }");
        var names = new[]
        {
            ".PowerForge.Value.artifact-publish.lock",
            ".PowerForge.Value.artifact-staging-owned",
            ".PowerForge.Value.artifact-backup-owned"
        };

        foreach (var name in names)
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                    fixture.ScriptPath,
                    fixture.OutputPath,
                    name,
                    PowerShellCompilationArtifactKind.Library,
                    PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true)));

            Assert.Contains("reserved publication-control namespace", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
        }
    }

    [Fact]
    public void Build_RejectsLinkedSourceAliasBeforeReplacingItsPhysicalOutputEntry()
    {
        var container = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var outputPath = Path.Combine(container, "output");
        var physicalSourceDirectory = Path.Combine(outputPath, "PowerForge.LinkedAlias");
        var linkedSourceDirectory = Path.Combine(container, "linked-source");
        var physicalSourcePath = Path.Combine(physicalSourceDirectory, "input.ps1");
        const string source = "function Get-Value { return 1 }";
        Directory.CreateDirectory(physicalSourceDirectory);
        File.WriteAllText(physicalSourcePath, source);
        try
        {
            Directory.CreateSymbolicLink(linkedSourceDirectory, physicalSourceDirectory);
        }
        catch (UnauthorizedAccessException)
        {
            Directory.Delete(container, recursive: true);
            return;
        }
        catch (PlatformNotSupportedException)
        {
            Directory.Delete(container, recursive: true);
            return;
        }

        try
        {
            var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                Path.Combine(linkedSourceDirectory, "input.ps1"),
                outputPath,
                "PowerForge.LinkedAlias",
                PowerShellCompilationArtifactKind.Library,
                PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

            Assert.False(result.Succeeded);
            Assert.Contains("symbolic link or junction", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(physicalSourcePath));
            Assert.Equal(source, File.ReadAllText(physicalSourcePath));
        }
        finally
        {
            try { Directory.Delete(linkedSourceDirectory); } catch { }
            try { Directory.Delete(container, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Build_HybridModulePreservesManifestAllowanceForConditionalExports()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-PublicValue { return 7 }; if ($true) { Export-ModuleMember -Function Get-PublicValue }",
            ".psm1");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @('Get-PublicValue'); CmdletsToExport = @(); VariablesToExport = @(); AliasesToExport = @() }");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ConditionalManifestExport",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; Get-PublicValue");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("7", run.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_MultiFilePackagedExecutableUsesManagedEntryPathWhenLaunchedThroughDotnet()
    {
        using var fixture = ArtifactFixture.Create(
            "param([string] $DefaultPath = $PSCommandPath, [string] $DefaultRoot = $PSScriptRoot); $DefaultPath; $DefaultRoot; $PSCommandPath; $PSScriptRoot");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DotnetEntryPath",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package, allowUnreviewedDependencyResolution: true)
        {
            SingleFile = false
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var assemblyPath = Assert.Single(result.Manifest!.Files, file => file.Role == "GeneratedAssembly").Path;
        var run = Run("dotnet", assemblyPath);
        Assert.Equal(0, run.ExitCode);
        var output = run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, output.Length);
        AssertPathsEqual(assemblyPath, output[0]);
        AssertPathsEqual(Path.GetDirectoryName(assemblyPath)!, output[1]);
        AssertPathsEqual(assemblyPath, output[2]);
        AssertPathsEqual(Path.GetDirectoryName(assemblyPath)!, output[3]);
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_StrictExecutableResolvesUniqueParameterAbbreviation()
    {
        using var fixture = ArtifactFixture.Create("param([string] $Name, [string] $Number); return $Name");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedAbbreviation",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = Run(result.ArtifactPath!, "-Na", "Ada");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("Ada", run.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);

        var ambiguous = Run(result.ArtifactPath!, "-N", "Ada");
        Assert.Equal(1, ambiguous.ExitCode);
        Assert.Contains("ambiguous", ambiguous.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("$Other = $Value++")]
    [InlineData("return ($Value++ + 1)")]
    [InlineData("if ($Value++) { return 1 }")]
    public void Analyze_RoutesValueProducingIncrementContextsToFallback(string statement)
    {
        using var fixture = ArtifactFixture.Create($"function Invoke-ValueContext {{ [int] $Value = 1; {statement} }}");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.False(unit.IsCompilable);
        Assert.NotEmpty(unit.Diagnostics);
    }

    [Fact]
    public void Build_StrictLibraryTreatsIncrementAndDecrementAsOutputFreeMutations()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-Increment { [int] $Value = 1; $Value++; return $Value++ } " +
            "function Get-LoopValue { [int] $Sum = 0; for ([int] $Index = 0; $Index -lt 3; $Index++) { $Sum += $Index }; return $Sum }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.IncrementSemantics",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var assembly = System.Reflection.Assembly.LoadFile(result.ArtifactPath!);
        var type = assembly.GetType("PowerForge.Compiled.PowerForge_IncrementSemanticsMethods", throwOnError: true)!;
        var increment = type.GetMethod("Invoke_Increment")!;
        Assert.Equal(typeof(void), increment.ReturnType);
        Assert.Null(increment.Invoke(null, null));
        Assert.Equal(3, type.GetMethod("Get_LoopValue")!.Invoke(null, null));
    }

    [Fact]
    public void Build_StrictLibraryEmitsOversizedIntegerAsBigIntegerParse()
    {
        const string value = "1234567890123456789012345678901234567890";
        using var fixture = ArtifactFixture.Create($"function Get-BigValue {{ return {value}n }}");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.BigIntegerLiteral",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var assembly = System.Reflection.Assembly.LoadFile(result.ArtifactPath!);
        var type = assembly.GetType("PowerForge.Compiled.PowerForge_BigIntegerLiteralMethods", throwOnError: true)!;
        Assert.Equal(
            System.Numerics.BigInteger.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
            type.GetMethod("Get_BigValue")!.Invoke(null, null));
    }

    [Fact]
    public void Build_StrictLibraryPreservesObservablePowerShellArrayTypes()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-LiteralType { $Values = 1, 2; return $Values.GetType().FullName } " +
            "function Get-ExpressionType { $Values = @(1, 2); return $Values.GetType().FullName } " +
            "function Get-ExplicitType { [int[]] $Values = @(1, 2); return $Values.GetType().FullName }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ArrayRuntimeTypes",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var assembly = System.Reflection.Assembly.LoadFile(result.ArtifactPath!);
        var type = assembly.GetType("PowerForge.Compiled.PowerForge_ArrayRuntimeTypesMethods", throwOnError: true)!;
        Assert.Equal("System.Object[]", type.GetMethod("Get_LiteralType")!.Invoke(null, null));
        Assert.Equal("System.Object[]", type.GetMethod("Get_ExpressionType")!.Invoke(null, null));
        Assert.Equal("System.Int32[]", type.GetMethod("Get_ExplicitType")!.Invoke(null, null));
    }

    [Fact]
    public void Build_PackagedExecutableRecognizesCommonSwitchAliases()
    {
        using var fixture = ArtifactFixture.Create(
            "[CmdletBinding()] param([string] $Name); Write-Verbose 'verbose-record'; Write-Debug 'debug-record'; return $Name");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.CommonSwitchAliases",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = Run(result.ArtifactPath!, "-vb", "-db", "Ada");
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("VERBOSE: verbose-record", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("DEBUG: debug-record", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Ada", run.StandardOutput, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);

        var abbreviatedRun = Run(result.ArtifactPath!, "-v", "Eve");
        Assert.Equal(0, abbreviatedRun.ExitCode);
        Assert.Contains("VERBOSE: verbose-record", abbreviatedRun.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Eve", abbreviatedRun.StandardOutput, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(abbreviatedRun.StandardError), abbreviatedRun.StandardError);
    }
}
