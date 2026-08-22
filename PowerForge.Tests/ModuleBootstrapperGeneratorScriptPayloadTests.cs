using PowerForge;

public partial class ModuleBootstrapperGeneratorTests
{
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("throw 'expected merged source failure'")]
    [InlineData("function Invoke-BrokenSource {")]
    public void InlineMergedScriptPayload_ContinuesAfterPerSourceImportFailure(string failingSource)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-source-failure-" + Guid.NewGuid().ToString("N"));
        var fixtureRoot = Path.Combine(root, "Fixture");
        var moduleRoot = Path.Combine(root, "Module");
        var libRoot = Directory.CreateDirectory(Path.Combine(moduleRoot, "Lib", "Core")).FullName;
        var publicRoot = Directory.CreateDirectory(Path.Combine(moduleRoot, "Public")).FullName;

        try
        {
            var fixtureAssembly = BuildFixtureProject(
                fixtureRoot,
                "SourceFailureFixture",
                "DemoModule",
                "namespace SourceFailureFixture; public static class Marker { public static string Value => \"binary\"; }");
            File.Copy(fixtureAssembly, Path.Combine(libRoot, "DemoModule.dll"), overwrite: true);
            File.WriteAllText(Path.Combine(publicRoot, "A-FailingSource.ps1"), failingSource);
            File.WriteAllText(
                Path.Combine(publicRoot, "B-ContinuedSource.ps1"),
                "function Get-AfterSourceFailure { 'continued' }");

            var exports = new ExportSet(
                new[] { "Get-AfterSourceFailure" },
                Array.Empty<string>(),
                Array.Empty<string>());
            var sources = ModuleMergeComposer.BuildSources(
                moduleRoot,
                "DemoModule",
                information: null,
                exports,
                fixRelativePaths: false,
                exportAssemblies: new[] { "DemoModule.dll" });

            ModuleBootstrapperGenerator.Generate(
                moduleRoot,
                "DemoModule",
                exports,
                new[] { "DemoModule.dll" },
                handleRuntimes: false,
                useAssemblyLoadContext: false);
            var bootstrapperPath = Path.Combine(moduleRoot, "DemoModule.psm1");
            ModuleBootstrapperGenerator.InlineMergedScriptPayload(bootstrapperPath, sources.MergedScriptContent);

            var processStartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "pwsh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            processStartInfo.ArgumentList.Add("-NoLogo");
            processStartInfo.ArgumentList.Add("-NoProfile");
            processStartInfo.ArgumentList.Add("-NonInteractive");
            processStartInfo.ArgumentList.Add("-ExecutionPolicy");
            processStartInfo.ArgumentList.Add("Bypass");
            processStartInfo.ArgumentList.Add("-Command");
            processStartInfo.ArgumentList.Add(
                "Import-Module -Name '" + bootstrapperPath.Replace("'", "''", StringComparison.Ordinal) +
                "' -Force -ErrorAction Continue; " +
                "if ((Get-AfterSourceFailure) -ne 'continued') { throw 'The later merged source was not loaded.' }; " +
                "'continued-after-error'");

            using var process = System.Diagnostics.Process.Start(processStartInfo)!;
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(
                process.ExitCode == 0,
                $"Generated module import failed.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
            Assert.Contains("continued-after-error", standardOutput, StringComparison.Ordinal);
            Assert.Contains("Failed to import merged module source", standardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
