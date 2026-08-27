using System.Diagnostics;

namespace PowerForge.Tests;

public sealed class PowerShellCompilationAutomatedReviewRegressionTests
{
    [Fact]
    public void Build_StrictExecutableBindsExplicitEmptyCollection()
    {
        using var fixture = Fixture.Create(
            "param([Parameter(Mandatory)][AllowEmptyCollection()][string[]] $Values); return $Values.Length");
        var result = BuildExecutable(fixture, "PowerForge.EmptyCollection");

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var empty = Run(result.ArtifactPath!, "--Values");
        var populated = Run(result.ArtifactPath!, "--Values", "one", "--Values", "two");
        var omitted = Run(result.ArtifactPath!);

        Assert.Equal((0, "0", string.Empty), Normalize(empty));
        Assert.Equal((0, "2", string.Empty), Normalize(populated));
        Assert.NotEqual(0, omitted.ExitCode);
        Assert.Contains("Required parameter", omitted.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_StrictExecutableRejectsExplicitEmptyCollectionWhenNotNullOrEmptyIsRequired()
    {
        using var fixture = Fixture.Create(
            "param([Parameter(Mandatory)][AllowEmptyCollection()][ValidateNotNullOrEmpty()][string[]] $Values); return $Values.Length");
        var result = BuildExecutable(fixture, "PowerForge.ValidatedEmptyCollection");

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var empty = Run(result.ArtifactPath!, "--Values");
        var populated = Run(result.ArtifactPath!, "--Values", "one");

        Assert.NotEqual(0, empty.ExitCode);
        Assert.Contains("does not allow null or empty values", empty.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal((0, "1", string.Empty), Normalize(populated));
    }

    [Fact]
    public void Analyze_StrictExecutableRejectsBinaryOnlyParameterMetadata()
    {
        using var fixture = Fixture.Create(
            "[CmdletBinding()] param([Parameter(HelpMessage='Pattern')][SupportsWildcards()][string] $Name); return $Name");
        var executable = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net8.0",
            capabilities: PowerShellCompilationCapabilities.TypedExecutable));
        var binary = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net8.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));

        Assert.False(Assert.Single(Assert.Single(executable.Files).Units).IsCompilable);
        Assert.True(Assert.Single(Assert.Single(binary.Files).Units).IsCompilable);
    }

    [Fact]
    public void Analyze_JaggedArrayIsNotAProcessArgument()
    {
        using var fixture = Fixture.Create("param([int[][]] $Values); return $Values.Length");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net8.0",
            capabilities: PowerShellCompilationCapabilities.TypedExecutable));
        var parameter = Assert.Single(Assert.Single(Assert.Single(plan.Files).Units).Parameters);

        Assert.False(parameter.TypeCapabilities.HasFlag(PowerShellCompilationParameterTypeCapability.ProcessArgument));
        Assert.False(Assert.Single(Assert.Single(plan.Files).Units).IsCompilable);
    }

    [Fact]
    public void Build_StrictExecutableValidatesOmittedLiteralDefault()
    {
        using var fixture = Fixture.Create("param([ValidateRange(1,5)][int] $Value = 7); return $Value");
        var result = BuildExecutable(fixture, "PowerForge.InvalidDefault");

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var omitted = Run(result.ArtifactPath!);
        Assert.NotEqual(0, omitted.ExitCode);
        Assert.Contains("outside", omitted.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_BinaryModulePreservesAuthoredOutputType()
    {
        using var fixture = Fixture.Create(
            "function Get-DeclaredValue { [OutputType([string[]])] param() return 'Ada' }; Export-ModuleMember -Function Get-DeclaredValue",
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.DeclaredOutput", "CompiledPowerShell", "net8.0");
        Assert.True(typed.Methods.Length == 1, string.Join(Environment.NewLine, typed.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        var method = typed.Methods[0];
        Assert.Equal(typeof(string[]).FullName, method.DeclaredOutputType);

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DeclaredOutput",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var path = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{path}' -Force; (Get-Command Get-DeclaredValue).OutputType[0].Type.FullName");
        Assert.Equal((0, typeof(string[]).FullName, string.Empty), Normalize(run));
    }

    [Fact]
    public void Build_BinaryModuleAvoidsAuthoredTemporaryIdentifierCollisions()
    {
        using var fixture = Fixture.Create(
            "function Test-GeneratedName { param([string] $__pf_wildcard_left_0, [string] $Text) " +
            "return $Text -like $__pf_wildcard_left_0 }; Export-ModuleMember -Function Test-GeneratedName",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TemporaryIdentifiers",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var path = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{path}' -Force; Test-GeneratedName -__pf_wildcard_left_0 'A*' -Text 'Ada'");
        Assert.Equal((0, "True", string.Empty), Normalize(run));
    }

    [Fact]
    public void Analyze_SwitchHostTypesStayBinaryModuleOnly()
    {
        using var fixture = Fixture.Create(
            "function Test-SwitchTypes { param([switch[]] $Apples) return [switch] $true }",
            ".psm1");
        var binary = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net8.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));
        var executable = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net8.0",
            capabilities: PowerShellCompilationCapabilities.TypedExecutable));
        var artifact = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.SwitchHostTypes",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        var binaryUnit = Assert.Single(Assert.Single(binary.Files).Units);
        Assert.True(binaryUnit.IsCompilable, string.Join(Environment.NewLine, binaryUnit.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.False(Assert.Single(Assert.Single(executable.Files).Units).IsCompilable);
        Assert.True(artifact.Succeeded, artifact.Error + Environment.NewLine + artifact.BuildOutput);
    }

    [Fact]
    public void PublicModelConstructorRetainsNamedOptionalArguments()
    {
        var method = new PowerShellCompiledMethod(
            "Get-Compatible",
            "Get_Compatible",
            typeof(string).FullName!,
            Array.Empty<PowerShellCompilationParameter>(),
            1,
            sourcePath: null,
            requiresPowerShellStreams: false,
            commandBinding: null,
            requiresPowerShellRuntimeState: true);

        Assert.True(method.RequiresPowerShellRuntimeState);
    }

    [Fact]
    public void Analyze_RejectsUnresolvableOutputType()
    {
        using var fixture = Fixture.Create("function Get-Value { [OutputType([Missing.Type])] param() return 1 }", ".psm1");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net8.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));

        Assert.False(Assert.Single(Assert.Single(plan.Files).Units).IsCompilable);
    }

    [Fact]
    public void PublicModelConstructorsRetainPriorSignatures()
    {
        AssertConstructor<PowerShellCompilationParameter>(
            typeof(string), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(string[]),
            typeof(bool), typeof(PowerShellCompilationValidation[]));
        AssertConstructor<PowerShellCompilationParameter>(
            typeof(string), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(string[]),
            typeof(bool), typeof(PowerShellCompilationValidation[]), typeof(PowerShellCompilationParameterTypeCapability),
            typeof(PowerShellCompilationParameterBinding[]), typeof(bool), typeof(bool), typeof(bool));
        AssertConstructor<PowerShellCompilationParameter>(
            typeof(string), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(string[]),
            typeof(bool), typeof(PowerShellCompilationValidation[]), typeof(PowerShellCompilationParameterTypeCapability),
            typeof(PowerShellCompilationParameterBinding[]), typeof(bool), typeof(bool), typeof(bool),
            typeof(PowerShellCompilationLiteral));
        AssertConstructor<PowerShellCompiledMethod>(
            typeof(string), typeof(string), typeof(string), typeof(PowerShellCompilationParameter[]), typeof(int),
            typeof(string), typeof(bool), typeof(bool), typeof(string[]), typeof(bool), typeof(bool));
        AssertConstructor<PowerShellCompiledMethod>(
            typeof(string), typeof(string), typeof(string), typeof(PowerShellCompilationParameter[]), typeof(int),
            typeof(string), typeof(bool), typeof(bool), typeof(string[]), typeof(bool), typeof(bool),
            typeof(PowerShellCompilationCommandBinding), typeof(bool));
        AssertConstructor<PowerShellCompilationCensusProduct>(
            typeof(string), typeof(string), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(double),
            typeof(PowerShellCompilationCensusBlocker[]), typeof(PowerShellCompilationFeatureImpact[]),
            typeof(PowerShellCompilationDependencySummary[]), typeof(PowerShellCompilationResourceSummary));
        AssertConstructor<PowerShellCompilationCensusProduct>(
            typeof(string), typeof(string), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(double),
            typeof(PowerShellCompilationCensusBlocker[]), typeof(PowerShellCompilationFeatureImpact[]),
            typeof(PowerShellCompilationDependencySummary[]), typeof(PowerShellCompilationResourceSummary),
            typeof(PowerShellCompilationCoverageBreakdown), typeof(string), typeof(PowerShellCompilationFeatureImpact[]));
        AssertConstructor<PowerShellCompilationCensusResult>(
            typeof(string), typeof(PowerShellCompilationCensusProduct[]), typeof(PowerShellCompilationCensusRegression[]),
            typeof(PowerShellCompilationFeatureImpact[]), typeof(PowerShellCompilationFeaturePair[]));
        AssertConstructor<PowerShellCompilationCensusResult>(
            typeof(string), typeof(PowerShellCompilationCensusProduct[]), typeof(PowerShellCompilationCensusRegression[]),
            typeof(PowerShellCompilationFeatureImpact[]), typeof(PowerShellCompilationFeaturePair[]),
            typeof(PowerShellCompilationCensusSourceDrift[]), typeof(PowerShellCompilationFeatureImpact[]),
            typeof(PowerShellCompilationFeaturePair[]));
    }

    private static void AssertConstructor<T>(params Type[] parameterTypes)
        => Assert.NotNull(typeof(T).GetConstructor(parameterTypes));

    private static PowerShellCompilationBuildResult BuildExecutable(Fixture fixture, string name)
        => new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            name,
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            SingleFile = false,
            EmitSource = true
        });

    private static (int ExitCode, string StandardOutput, string StandardError) Run(string fileName, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(120_000), $"Process '{fileName}' timed out.");
        return (process.ExitCode, stdout, stderr);
    }

    private static (int ExitCode, string StandardOutput, string StandardError) Normalize(
        (int ExitCode, string StandardOutput, string StandardError) result)
        => (result.ExitCode, result.StandardOutput.Trim(), result.StandardError.Trim());

    private sealed class Fixture : IDisposable
    {
        private Fixture(string rootPath, string scriptPath)
        {
            RootPath = rootPath;
            ScriptPath = scriptPath;
            OutputPath = Path.Combine(rootPath, "output");
        }

        internal string RootPath { get; }
        internal string ScriptPath { get; }
        internal string OutputPath { get; }

        internal static Fixture Create(string source, string extension = ".ps1")
        {
            var root = Path.Combine(Path.GetTempPath(), "PowerForgeAutomatedReview-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var sourcePath = Path.Combine(root, "Source" + extension);
            File.WriteAllText(sourcePath, source);
            return new Fixture(root, sourcePath);
        }

        public void Dispose()
        {
            try { Directory.Delete(RootPath, recursive: true); } catch { }
            var siblingArtifacts = Path.Combine(Directory.GetParent(RootPath)?.FullName ?? RootPath, "artifacts", new DirectoryInfo(RootPath).Name);
            try { Directory.Delete(siblingArtifacts, recursive: true); } catch { }
        }
    }
}
