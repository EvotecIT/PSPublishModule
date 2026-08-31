using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void Corpus_StrictApplicationQualifiesDeliveredResourceAndControlledFailure()
    {
        var corpusRoot = FindGenericCorpusRoot();
        var entryPoint = Path.Combine(corpusRoot, "StrictApplication", "Main.ps1");
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
                "GenericCompilerStrictApplicationQualification",
                resolved.Kind,
                resolved.Mode,
                allowUnreviewedDependencyResolution: true)
            {
                CompilationSourcePaths = resolved.CompilationSourceFiles,
                RuntimeSourcePaths = resolved.SourceFiles,
                TargetFramework = "net8.0",
                IncludeResource = new[] { "report-label.txt" }
            });

            Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
            Assert.False(result.Manifest!.RequiresPowerShellRuntime);
            Assert.Equal(4, result.Manifest.SourceFiles.Length);
            Assert.Equal(1, result.Manifest.ResourceSummary.IncludedFiles);
            var deliveredResource = Path.Combine(Path.GetDirectoryName(result.ArtifactPath!)!, "report-label.txt");
            Assert.True(File.Exists(deliveredResource));
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(corpusRoot, "StrictApplication", "report-label.txt")),
                File.ReadAllBytes(deliveredResource));

            var resourceRun = Run(result.ArtifactPath!, "-ResourcePath", deliveredResource);
            Assert.Equal((0, "READY|42|15|3|high|resource-ok", string.Empty),
                (resourceRun.ExitCode, resourceRun.StandardOutput.Trim(), resourceRun.StandardError.Trim()));

            var failureRun = Run(result.ArtifactPath!, "-Fail");
            Assert.NotEqual(0, failureRun.ExitCode);
            Assert.Contains("strict-application-requested-failure", failureRun.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(output, recursive: true); } catch { }
        }
    }
}
