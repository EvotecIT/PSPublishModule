using PowerForge;

public partial class ModuleBootstrapperGeneratorTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void InlineMergedScriptPayload_LoadsRebasedRequiredAssemblyBeforeCompilingSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-required-assembly-" + Guid.NewGuid().ToString("N"));
        var moduleRoot = Path.Combine(root, "Module");
        var libRoot = Directory.CreateDirectory(Path.Combine(moduleRoot, "Lib")).FullName;
        var publicRoot = Directory.CreateDirectory(Path.Combine(moduleRoot, "Public")).FullName;

        try
        {
            var moduleAssembly = BuildFixtureProject(
                Path.Combine(root, "ModuleFixture"),
                "RequiredAssemblyModuleFixture",
                "DemoModule",
                "namespace RequiredAssemblyModuleFixture; public static class Marker { public static string Value => \"binary\"; }");
            var requiredAssembly = BuildFixtureProject(
                Path.Combine(root, "RequiredFixture"),
                "RequiredTypesFixture",
                "RequiredTypes",
                "namespace RequiredTypes; public class Base { public string Read() => \"required-type\"; }");
            File.Copy(moduleAssembly, Path.Combine(libRoot, "DemoModule.dll"), overwrite: true);
            File.Copy(requiredAssembly, Path.Combine(libRoot, "RequiredTypes.dll"), overwrite: true);
            File.WriteAllText(
                Path.Combine(publicRoot, "Get-RequiredType.ps1"),
                "#requires -Assembly ../Lib/RequiredTypes.dll" + Environment.NewLine +
                "class PowerForgeRequiredDerived : RequiredTypes.Base { }" + Environment.NewLine +
                "function Get-RequiredType { [PowerForgeRequiredDerived]::new().Read() }");

            var exports = new ExportSet(new[] { "Get-RequiredType" }, Array.Empty<string>(), Array.Empty<string>());
            var sources = ModuleMergeComposer.BuildSources(
                moduleRoot,
                "DemoModule",
                information: null,
                exports,
                fixRelativePaths: true,
                exportAssemblies: new[] { "DemoModule.dll" });
            Assert.Contains("#requires -Assembly ./Lib/RequiredTypes.dll", sources.MergedScriptContent, StringComparison.Ordinal);

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
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            processStartInfo.ArgumentList.Add("-NoLogo");
            processStartInfo.ArgumentList.Add("-NoProfile");
            processStartInfo.ArgumentList.Add("-NonInteractive");
            processStartInfo.ArgumentList.Add("-Command");
            processStartInfo.ArgumentList.Add(
                "Import-Module -Name '" + bootstrapperPath.Replace("'", "''", StringComparison.Ordinal) +
                "' -Force -ErrorAction Stop; Get-RequiredType");

            using var process = System.Diagnostics.Process.Start(processStartInfo)!;
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(
                process.ExitCode == 0,
                $"Required-assembly source import failed.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
            Assert.Contains("required-type", standardOutput, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void RegularBinaryLoader_ContinuesAfterRecoverableConfiguredAssemblyFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-recoverable-assembly-" + Guid.NewGuid().ToString("N"));
        var libRoot = Directory.CreateDirectory(Path.Combine(root, "Lib", "Core")).FullName;

        try
        {
            File.WriteAllText(Path.Combine(libRoot, "Broken.dll"), "not a managed assembly");
            var moduleAssembly = BuildFixtureProject(
                Path.Combine(root, "Fixture"),
                "RecoverableAssemblyFixture",
                "DemoModule",
                "namespace DemoModule { public static class Initialize { public static string Read() => \"later-assembly\"; } }");
            File.Copy(moduleAssembly, Path.Combine(libRoot, "DemoModule.dll"), overwrite: true);

            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
                new[] { "Broken.dll", "DemoModule.dll" },
                handleRuntimes: false,
                useAssemblyLoadContext: false);
            var bootstrapperPath = Path.Combine(root, "DemoModule.psm1");

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
            processStartInfo.ArgumentList.Add("-Command");
            processStartInfo.ArgumentList.Add(
                "$ErrorActionPreference = 'Continue'; Import-Module -Name '" +
                bootstrapperPath.Replace("'", "''", StringComparison.Ordinal) +
                "' -Force; [DemoModule.Initialize]::Read()");

            using var process = System.Diagnostics.Process.Start(processStartInfo)!;
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(
                process.ExitCode == 0,
                $"Recoverable multi-assembly import failed.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
            Assert.Contains("later-assembly", standardOutput, StringComparison.Ordinal);
            Assert.Contains("Importing module Broken.dll failed", standardOutput + standardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("#requires -PSEdition Desktop")]
    [InlineData("#requires -Version 99.0")]
    public void InlineMergedScriptPayload_KeepsRequiresScopedToItsSource(string requiresDirective)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-source-requires-" + Guid.NewGuid().ToString("N"));
        var fixtureRoot = Path.Combine(root, "Fixture");
        var moduleRoot = Path.Combine(root, "Module");
        var libRoot = Directory.CreateDirectory(Path.Combine(moduleRoot, "Lib", "Core")).FullName;
        var publicRoot = Directory.CreateDirectory(Path.Combine(moduleRoot, "Public")).FullName;

        try
        {
            var fixtureAssembly = BuildFixtureProject(
                fixtureRoot,
                "SourceRequiresFixture",
                "DemoModule",
                "namespace SourceRequiresFixture; public static class Marker { public static string Value => \"binary\"; }");
            File.Copy(fixtureAssembly, Path.Combine(libRoot, "DemoModule.dll"), overwrite: true);
            File.WriteAllText(
                Path.Combine(publicRoot, "A-DesktopOnly.ps1"),
                requiresDirective + Environment.NewLine +
                "function Get-DesktopOnlySource { 'desktop' }");
            File.WriteAllText(
                Path.Combine(publicRoot, "B-CoreCompatible.ps1"),
                "function Get-CoreCompatibleSource { 'core' }");

            var exports = new ExportSet(
                new[] { "Get-DesktopOnlySource", "Get-CoreCompatibleSource" },
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
            var bootstrapper = File.ReadAllText(bootstrapperPath);
            var globalPreamble = bootstrapper.Substring(
                bootstrapper.IndexOf("# PowerForge script preamble begin", StringComparison.Ordinal),
                bootstrapper.IndexOf("# PowerForge script preamble end", StringComparison.Ordinal) -
                bootstrapper.IndexOf("# PowerForge script preamble begin", StringComparison.Ordinal));
            Assert.DoesNotContain("#requires", globalPreamble, StringComparison.OrdinalIgnoreCase);

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
                "if ((Get-CoreCompatibleSource) -ne 'core') { throw 'The compatible source was not loaded.' }; " +
                "if (Get-Command Get-DesktopOnlySource -ErrorAction SilentlyContinue) { throw 'The incompatible source unexpectedly loaded.' }; " +
                "'source-requires-scoped'");

            using var process = System.Diagnostics.Process.Start(processStartInfo)!;
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(
                process.ExitCode == 0,
                $"Generated module import failed.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
            Assert.Contains("source-requires-scoped", standardOutput, StringComparison.Ordinal);
            Assert.Contains("Failed to import merged module source", standardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

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
