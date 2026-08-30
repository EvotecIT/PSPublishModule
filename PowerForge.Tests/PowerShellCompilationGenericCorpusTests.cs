using System.Text.Json;
using System.Text.Json.Serialization;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void Corpus_CensusMatchesPortablePostEmissionBaseline()
    {
        var corpusRoot = FindGenericCorpusRoot();
        var manifest = Path.Combine(corpusRoot, "HybridModule", "Generic.Compiler.Corpus.psd1");
        var baselinePath = Path.Combine(corpusRoot, "census-baseline.net10.json");
        var baseline = JsonSerializer.Deserialize<PowerShellCompilationCensusResult>(
            File.ReadAllText(baselinePath),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            });

        Assert.NotNull(baseline);
        var result = new PowerShellCompilationCensusRunner().Run(new[] { manifest }, "net10.0", baseline);

        Assert.True(result.Passed, string.Join(Environment.NewLine, result.Regressions.Select(static regression =>
            $"{regression.Product}: {regression.Metric} {regression.Baseline} -> {regression.Current}")));
        Assert.Empty(result.SourceDrifts);
        Assert.True(result.PostEmissionEvaluated);
        Assert.Equal(8, result.TotalFunctions);
        Assert.Equal(8, result.EmittedFunctions);
        Assert.Equal(0, result.DroppedEligibleFunctions);
        Assert.Equal(1, result.RuntimeFallbackUnits);
        Assert.Empty(result.FunctionFrontier);
    }

    [Fact]
    public void Corpus_HybridModulePreservesTypedAndFallbackContracts()
    {
        var corpusRoot = FindGenericCorpusRoot();
        var manifest = Path.Combine(corpusRoot, "HybridModule", "Generic.Compiler.Corpus.psd1");
        var output = CreateGenericCorpusOutput();
        try
        {
            var resolved = new PowerShellCompilationInputResolver().Resolve(
                manifest,
                PowerShellCompilationArtifactKind.BinaryModule,
                PowerShellCompilationMode.Hybrid);
            var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                resolved.SourcePath,
                output,
                "GenericCompilerCorpus",
                resolved.Kind,
                resolved.Mode, allowUnreviewedDependencyResolution: true)
            {
                ModuleManifestPath = resolved.ModuleManifestPath,
                CompilationSourcePaths = resolved.CompilationSourceFiles,
                RuntimeSourcePaths = resolved.SourceFiles,
                TargetFramework = "net8.0",
                EmitSource = true
            });

            Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
            Assert.Equal(8, result.Manifest!.CompiledMethods);
            Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
            Assert.True(result.Manifest.UsesPowerShellRuntimeFallback);
            Assert.True(result.Manifest.AllowsPowerShellRuntimeEvaluation);
            var ledger = Assert.IsType<PowerShellCompilationUnitDispositionLedger>(result.Manifest.UnitDispositionLedger);
            var commandRegion = Assert.Single(ledger.Entries, static entry => entry.Name == "Get-CommandText");
            Assert.True(commandRegion.EmittedClrMethod);
            Assert.True(commandRegion.RuntimeRouted);
            Assert.Equal(1, commandRegion.RuntimeCommandRegions);
            const string proof =
                "$env:POWERFORGE_COMPILER_CORPUS = 'runtime'; " +
                "Measure-TextScore -Text Ada; Get-CountdownValue -Number 4; Get-RuntimeState -WhatIf; " +
                "Get-CommandText -Text Ada; Test-TokenPattern -Token alpha; Get-EnvironmentBoundary; " +
                "(Get-ObjectShape) -join '|'; (Get-CollectionShape) -join '|'";
            var original = RunCorpusModule(manifest, proof);
            var compiled = RunCorpusModule(result.ArtifactPath!, proof);

            Assert.Equal((0, string.Empty), (original.ExitCode, original.StandardError.Trim()));
            Assert.Equal((0, string.Empty), (compiled.ExitCode, compiled.StandardError.Trim()));
            Assert.Equal(original.StandardOutput.Trim(), compiled.StandardOutput.Trim());
            Assert.Equal(
                new[] { "8", "0", "whatif", "ADA", "True", "runtime", "Grace|Ready|2", "alpha|omega|2" },
                compiled.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs"));
            Assert.Contains("__invokePowerShellCapture", generated, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(output, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData("StrictProgram", "8", 3)]
    [InlineData("StrictCollections", "2", 2)]
    [InlineData("StrictSwitch", "20", 2)]
    public void Corpus_StrictProgramsBuildAndRunWithoutPowerShellRuntime(
        string programDirectory,
        string expectedOutput,
        int expectedCompiledMethods)
    {
        var corpusRoot = FindGenericCorpusRoot();
        var entryPoint = Path.Combine(corpusRoot, programDirectory, "Main.ps1");
        var output = CreateGenericCorpusOutput();
        try
        {
            var resolved = new PowerShellCompilationInputResolver().Resolve(
                entryPoint,
                PowerShellCompilationArtifactKind.Executable,
                PowerShellCompilationMode.Strict);
            var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                resolved.SourcePath,
                output,
                "GenericCompiler" + programDirectory,
                resolved.Kind,
                resolved.Mode, allowUnreviewedDependencyResolution: true)
            {
                CompilationSourcePaths = resolved.CompilationSourceFiles,
                RuntimeSourcePaths = resolved.SourceFiles,
                TargetFramework = "net8.0",
                EmitSource = true
            });

            Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
            Assert.False(result.Manifest!.RequiresPowerShellRuntime);
            Assert.False(result.Manifest.UsesPowerShellRuntimeFallback);
            Assert.Equal(expectedCompiledMethods, result.Manifest.CompiledMethods);
            var original = Run("pwsh", "-NoProfile", "-NonInteractive", "-File", entryPoint);
            var compiled = Run(result.ArtifactPath!);

            Assert.Equal((0, expectedOutput, string.Empty), (original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()));
            Assert.Equal((0, expectedOutput, string.Empty), (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
        }
        finally
        {
            try { Directory.Delete(output, recursive: true); } catch { }
        }
    }

    private static string FindGenericCorpusRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Benchmarks", "PowerShellCompilation", "Corpus");
            if (File.Exists(Path.Combine(candidate, "census-baseline.net10.json")))
                return candidate;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Unable to locate the generic PowerShell compilation corpus.");
    }

    private static string CreateGenericCorpusOutput()
    {
        var output = Path.Combine(Path.GetTempPath(), "PowerForge Generic Compiler Corpus", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        return output;
    }

    private static (int ExitCode, string StandardOutput, string StandardError) RunCorpusModule(string modulePath, string proof)
        => Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{modulePath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {proof}");
}
