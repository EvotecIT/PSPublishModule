using PowerForge;

public partial class ModuleBootstrapperGeneratorTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void AssemblyLoadContextLoader_ContinuesAfterRecoverableConfiguredAssemblyFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-alc-recoverable-" + Guid.NewGuid().ToString("N"));
        var libRoot = Directory.CreateDirectory(Path.Combine(root, "Lib", "Core")).FullName;

        try
        {
            File.WriteAllText(Path.Combine(libRoot, "Broken.dll"), "not a managed assembly");
            var validAssembly = BuildFixtureProject(
                Path.Combine(root, "Fixture"),
                "AlcRecoverableFixture",
                "Auxiliary",
                "namespace Auxiliary; public static class Initialize { public static string Read() => \"later-alc-assembly\"; }");
            File.Copy(validAssembly, Path.Combine(libRoot, "Auxiliary.dll"), overwrite: true);

            ModuleBootstrapperGenerator.Generate(
                root,
                "Broken",
                new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
                new[] { "Broken.dll", "Auxiliary.dll" },
                handleRuntimes: false,
                useAssemblyLoadContext: true,
                targetFrameworks: new[] { "net8.0" });

            var result = RunGeneratedModulePowerShell(
                root,
                "$ErrorActionPreference = 'Continue'; Import-Module -Name '" +
                Path.Combine(root, "Broken.psm1").Replace("'", "''", StringComparison.Ordinal) +
                "' -Force; [Auxiliary.Initialize]::Read()");

            Assert.True(result.ExitCode == 0, result.StdOut + Environment.NewLine + result.StdErr);
            Assert.Contains("later-alc-assembly", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Importing module Broken.dll failed", result.StdOut + result.StdErr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void AssemblyLoadContextLoader_DeduplicatesReferencesToSameResolvedAssembly()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-alc-deduplicate-" + Guid.NewGuid().ToString("N"));
        var pluginRoot = Directory.CreateDirectory(Path.Combine(root, "Lib", "Plugins")).FullName;

        try
        {
            var moduleAssembly = BuildFixtureProject(
                Path.Combine(root, "Fixture"),
                "AlcDeduplicateFixture",
                "DemoModule",
                "namespace DemoModule; public static class Initialize { public static string Read() => \"deduplicated-alc\"; }");
            File.Copy(moduleAssembly, Path.Combine(pluginRoot, "DemoModule.dll"), overwrite: true);

            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
                new[] { "DemoModule.dll", "Lib/Plugins/DemoModule.dll" },
                handleRuntimes: false,
                useAssemblyLoadContext: true,
                targetFrameworks: new[] { "net8.0" });

            var bootstrapperPath = Path.Combine(root, "DemoModule.psm1");
            var bootstrapper = File.ReadAllText(bootstrapperPath);
            Assert.Contains("HashSet[string]", bootstrapper, StringComparison.Ordinal);
            var result = RunGeneratedModulePowerShell(
                root,
                "$ErrorActionPreference = 'Stop'; Import-Module -Name '" +
                bootstrapperPath.Replace("'", "''", StringComparison.Ordinal) +
                "' -Force; [DemoModule.Initialize]::Read()");

            Assert.True(result.ExitCode == 0, result.StdOut + Environment.NewLine + result.StdErr);
            Assert.Contains("deduplicated-alc", result.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void AssemblyLoadContextLoader_IsCopiedBesideRootFallbackAssembly()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-alc-root-fallback-" + Guid.NewGuid().ToString("N"));
        var libRoot = Directory.CreateDirectory(Path.Combine(root, "Lib")).FullName;
        var futureCoreRoot = Directory.CreateDirectory(Path.Combine(libRoot, "Core-net99.0")).FullName;

        try
        {
            var moduleAssembly = BuildFixtureProject(
                Path.Combine(root, "Fixture"),
                "AlcRootFallbackFixture",
                "DemoModule",
                "namespace DemoModule; public static class Initialize { public static string Read() => \"root-fallback\"; }");
            File.Copy(moduleAssembly, Path.Combine(libRoot, "DemoModule.dll"), overwrite: true);
            File.Copy(moduleAssembly, Path.Combine(futureCoreRoot, "DemoModule.dll"), overwrite: true);

            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
                new[] { "DemoModule.dll" },
                handleRuntimes: false,
                useAssemblyLoadContext: true,
                targetFrameworks: new[] { "net8.0" });

            Assert.True(File.Exists(Path.Combine(libRoot, "DemoModule.ModuleLoadContext.dll")));
            Assert.True(File.Exists(Path.Combine(futureCoreRoot, "DemoModule.ModuleLoadContext.dll")));
            var result = RunGeneratedModulePowerShell(
                root,
                "$ErrorActionPreference = 'Stop'; Import-Module -Name '" +
                Path.Combine(root, "DemoModule.psm1").Replace("'", "''", StringComparison.Ordinal) +
                "' -Force; [DemoModule.Initialize]::Read()");

            Assert.True(result.ExitCode == 0, result.StdOut + Environment.NewLine + result.StdErr);
            Assert.Contains("root-fallback", result.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void AssemblyLoadContextLoader_IsCopiedBesideNestedFallbackWhenNamedCoreCopyIsIncompatible()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-alc-nested-fallback-" + Guid.NewGuid().ToString("N"));
        var pluginRoot = Directory.CreateDirectory(Path.Combine(root, "Lib", "Plugins")).FullName;
        var futureCoreRoot = Directory.CreateDirectory(Path.Combine(root, "Lib", "Core-net99.0")).FullName;

        try
        {
            var moduleAssembly = BuildFixtureProject(
                Path.Combine(root, "Fixture"),
                "AlcNestedFallbackFixture",
                "DemoModule",
                "namespace DemoModule; public static class Initialize { public static string Read() => \"nested-fallback\"; }");
            File.Copy(moduleAssembly, Path.Combine(pluginRoot, "DemoModule.dll"), overwrite: true);
            File.Copy(moduleAssembly, Path.Combine(futureCoreRoot, "DemoModule.dll"), overwrite: true);

            ModuleBootstrapperGenerator.Generate(
                root,
                "DemoModule",
                new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
                new[] { "DemoModule.dll" },
                handleRuntimes: false,
                useAssemblyLoadContext: true,
                targetFrameworks: new[] { "net8.0" });

            Assert.True(File.Exists(Path.Combine(pluginRoot, "DemoModule.ModuleLoadContext.dll")));
            Assert.True(File.Exists(Path.Combine(futureCoreRoot, "DemoModule.ModuleLoadContext.dll")));
            var result = RunGeneratedModulePowerShell(
                root,
                "$ErrorActionPreference = 'Stop'; Import-Module -Name '" +
                Path.Combine(root, "DemoModule.psm1").Replace("'", "''", StringComparison.Ordinal) +
                "' -Force; [DemoModule.Initialize]::Read()");

            Assert.True(result.ExitCode == 0, result.StdOut + Environment.NewLine + result.StdErr);
            Assert.Contains("nested-fallback", result.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void InlineMergedScriptPayload_RetainsDirectiveOnlySourceSegment()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-bootstrapper-directive-only-" + Guid.NewGuid().ToString("N"));
        var moduleRoot = Path.Combine(root, "Module");
        var libRoot = Directory.CreateDirectory(Path.Combine(moduleRoot, "Lib", "Core")).FullName;
        var publicRoot = Directory.CreateDirectory(Path.Combine(moduleRoot, "Public")).FullName;

        try
        {
            var moduleAssembly = BuildFixtureProject(
                Path.Combine(root, "ModuleFixture"),
                "DirectiveOnlyModuleFixture",
                "DemoModule",
                "namespace DirectiveOnlyModuleFixture; public static class Marker { public static string Value => \"binary\"; }");
            var requiredAssembly = BuildFixtureProject(
                Path.Combine(root, "RequiredFixture"),
                "DirectiveOnlyRequiredFixture",
                "RequiredTypes",
                "namespace RequiredTypes; public static class Marker { public static string Value => \"directive-dependency\"; }");
            File.Copy(moduleAssembly, Path.Combine(libRoot, "DemoModule.dll"), overwrite: true);
            File.Copy(requiredAssembly, Path.Combine(moduleRoot, "RequiredTypes.dll"), overwrite: true);
            File.WriteAllText(
                Path.Combine(publicRoot, "A-Dependency.ps1"),
                "#requires -Assembly ../RequiredTypes.dll");
            File.WriteAllText(
                Path.Combine(publicRoot, "B-Consumer.ps1"),
                "function Get-DirectiveDependency { [RequiredTypes.Marker]::Value }");

            var exports = new ExportSet(new[] { "Get-DirectiveDependency" }, Array.Empty<string>(), Array.Empty<string>());
            var sources = ModuleMergeComposer.BuildSources(
                moduleRoot,
                "DemoModule",
                information: null,
                exports,
                fixRelativePaths: true,
                exportAssemblies: new[] { "DemoModule.dll" });
            Assert.Equal(2, CountBootstrapperOccurrences(sources.MergedScriptContent, ModuleMergeComposer.MergedSourceStartMarker));

            ModuleBootstrapperGenerator.Generate(
                moduleRoot,
                "DemoModule",
                exports,
                new[] { "DemoModule.dll" },
                handleRuntimes: false,
                useAssemblyLoadContext: false);
            var bootstrapperPath = Path.Combine(moduleRoot, "DemoModule.psm1");
            ModuleBootstrapperGenerator.InlineMergedScriptPayload(bootstrapperPath, sources.MergedScriptContent);
            var result = RunGeneratedModulePowerShell(
                root,
                "$ErrorActionPreference = 'Stop'; Import-Module -Name '" +
                bootstrapperPath.Replace("'", "''", StringComparison.Ordinal) +
                "' -Force; Get-DirectiveDependency");

            Assert.True(result.ExitCode == 0, result.StdOut + Environment.NewLine + result.StdErr);
            Assert.Contains("directive-dependency", result.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunGeneratedModulePowerShell(string workingDirectory, string command)
    {
        var processStartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        processStartInfo.ArgumentList.Add("-NoLogo");
        processStartInfo.ArgumentList.Add("-NoProfile");
        processStartInfo.ArgumentList.Add("-NonInteractive");
        processStartInfo.ArgumentList.Add("-Command");
        processStartInfo.ArgumentList.Add(command);

        using var process = System.Diagnostics.Process.Start(processStartInfo)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, standardOutput, standardError);
    }

    private static int CountBootstrapperOccurrences(string content, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
