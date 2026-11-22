using System.Management.Automation.Runspaces;
using System.Text;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [Fact]
    public void ArtifactCmdletResolvesDuplicatePathsWithThePackageProfile()
    {
        using var fixture = OracleFixture.Create(". \"$PSScriptRoot/café.ps1\"");
        File.WriteAllText(Path.Combine(fixture.RootPath, "café.ps1"), "'helper'");

        var sessionState = InitialSessionState.CreateDefault();
        sessionState.Commands.Add(new SessionStateCmdletEntry("Build-PowerShellArtifact",
            typeof(PSPublishModule.BuildPowerShellArtifactCommand), helpFileName: null));
        using var runspace = RunspaceFactory.CreateRunspace(sessionState);
        runspace.Open();
        foreach (var paths in new[] { new[] { fixture.ScriptPath }, new[] { fixture.ScriptPath, fixture.ScriptPath } })
        {
            using var command = System.Management.Automation.PowerShell.Create();
            command.Runspace = runspace;
            command.AddCommand("Build-PowerShellArtifact").AddParameter("Path", paths)
                .AddParameter("TargetFramework", "net472").AddParameter("WhatIf");
            Assert.Empty(command.Invoke());
            Assert.Empty(command.Streams.Error);
        }
    }

    [PinnedSemanticHostFact]
    public async Task StrictSourceDecodingMatchesNativeFileInvocationOnBothPinnedHosts()
    {
        const string source = "return 'café'";
        var encodings = new[] { Encoding.Latin1, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true) };
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            foreach (var encoding in encodings)
            {
                using var fixture = OracleFixture.Create(source);
                File.WriteAllText(fixture.ScriptPath, source, encoding);
                var observerPath = Path.Combine(fixture.RootPath, "Observe.ps1");
                File.WriteAllText(observerPath, "[Console]::OutputEncoding = [Text.Encoding]::UTF8; & $args[0]");
                var host = StrictStreamingHostPath(pin) ??
                    Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
                var native = await ObserveExitedConsole(host, fixture.RootPath,
                    new[] { "-NoProfile", "-NonInteractive", "-File", observerPath, fixture.ScriptPath }, string.Empty);
                Assert.Equal(0, native.Code);
                Assert.Empty(native.Error);
                if (encoding.GetPreamble().Length != 0) Assert.Equal("café", native.Output.TrimEnd());

                var compiled = BuildStrictStreamingFixture(fixture, "StrictSourceEncoding", pin.ProfileId);
                Assert.True(compiled.Succeeded, compiled.Error + Environment.NewLine + compiled.BuildOutput);
                Assert.False(compiled.Manifest!.RequiresPowerShellRuntime);
                var observation = await ObserveExitedConsole(compiled.ArtifactPath!, fixture.RootPath, Array.Empty<string>(), string.Empty);
                Assert.Equal(native, observation);
            }
        }
    }

    [PinnedSemanticHostFact]
    public async Task StrictDotSourceClosureUsesTheSelectedProfileEncoding()
    {
        const string source = """
            . "$PSScriptRoot/café.ps1"
            Get-Value
            """;
        using var fixture = OracleFixture.Create(source);
        File.WriteAllText(fixture.ScriptPath, source, Encoding.Latin1);
        var legacyCodePage = CodePagesEncodingProvider.Instance.GetEncoding(0) ?? Encoding.UTF8;
        var suffix = new byte[] { 0xe9 };
        var legacyName = "caf" + legacyCodePage.GetString(suffix) + ".ps1";
        var modernName = "caf" + Encoding.UTF8.GetString(suffix) + ".ps1";
        File.WriteAllText(Path.Combine(fixture.RootPath, modernName), "function Get-Value { return 20 }");
        if (!string.Equals(legacyName, modernName, StringComparison.OrdinalIgnoreCase))
            File.WriteAllText(Path.Combine(fixture.RootPath, legacyName), "function Get-Value { return 10 }");

        const string legacyProfile = PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId;
        var desktopInput = new PowerShellCompilationInputResolver(legacyProfile).Resolve(fixture.ScriptPath,
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Strict);
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var input = new PowerShellCompilationInputResolver(pin.ProfileId).Resolve(fixture.ScriptPath,
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Strict);
            Assert.Equal(2, input.CompilationSourceFiles.Length);
            var plan = new PowerShellCompilationAnalyzer(Array.Empty<PowerShellCompilationCommandProviderContract>(), pin.ProfileId)
                .Analyze(desktopInput, PowerShellCompilationMode.Strict, "net10.0");
            Assert.Equal(input.CompilationSourceFiles.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase),
                plan.Files.Select(static file => file.FullPath).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase));
            var explanation = PowerShellCompilationExplainShaper.CreateFinalExplanation(desktopInput, plan, "net10.0");
            Assert.True(explanation.CanProceed);
            Assert.Equal(2, explanation.TypedUnits);
            var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                input.SourcePath, Path.Combine(fixture.RootPath, "strict"), "StrictEncodedDotSource",
                input.Kind, input.Mode, allowUnreviewedDependencyResolution: true)
            {
                TargetFramework = "net10.0",
                SemanticProfileId = pin.ProfileId,
                CompilationSourcePaths = input.CompilationSourceFiles,
                RuntimeSourcePaths = input.SourceFiles,
                SingleFile = false
            });
            Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
            Assert.True(build.Manifest!.DependencyClosureVerified);
            var host = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var native = await ObserveExitedConsole(host, fixture.RootPath,
                new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath }, string.Empty);
            Assert.Equal(0, native.Code);
            Assert.Empty(native.Error);
            Assert.Equal(native, await ObserveExitedConsole(build.ArtifactPath!, fixture.RootPath, Array.Empty<string>(), string.Empty));
        }
    }

    [PinnedSemanticHostFact]
    public async Task EncodedResourcesAndRetainedScriptsUseTheSameProfileOnBothPinnedHosts()
    {
        const string source = """
            . $PSScriptRoot/café.ps1
            Get-Value
            Get-Content -LiteralPath $PSScriptRoot/café.txt
            """;
        using var fixture = OracleFixture.Create(source);
        File.WriteAllText(fixture.ScriptPath, source, Encoding.Latin1);
        var legacyCodePage = CodePagesEncodingProvider.Instance.GetEncoding(0) ?? Encoding.UTF8;
        var suffix = new byte[] { 0xe9 };
        var legacyStem = "caf" + legacyCodePage.GetString(suffix);
        var modernStem = "caf" + Encoding.UTF8.GetString(suffix);
        foreach (var stem in new[] { modernStem, legacyStem }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var marker = stem == modernStem ? "modern" : "legacy";
            File.WriteAllText(Path.Combine(fixture.RootPath, stem + ".ps1"),
                "function Get-Value { '" + marker + "'; [int][char]'é' }", Encoding.Latin1);
            File.WriteAllText(Path.Combine(fixture.RootPath, stem + ".txt"), marker + "-resource");
        }

        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var input = new PowerShellCompilationInputResolver(pin.ProfileId).Resolve(fixture.ScriptPath,
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Package);
            var plan = new PowerShellCompilationAnalyzer(Array.Empty<PowerShellCompilationCommandProviderContract>(), pin.ProfileId)
                .Analyze(input, input.Mode, "net10.0");
            Assert.True(plan.CanProceed);
            var stem = pin.ProfileId == PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId ? legacyStem : modernStem;
            var inferred = Assert.Single(plan.Dependencies, candidate =>
                candidate.Discovery == PowerShellCompilationDependencyDiscovery.InferredLiteralResource);
            Assert.True(inferred.Exists);
            Assert.Equal(stem + ".txt", inferred.RelativePath);
            var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                input.SourcePath, Path.Combine(fixture.RootPath, "package"), "EncodedPackage",
                input.Kind, input.Mode)
            {
                TargetFramework = "net10.0",
                SemanticProfileId = pin.ProfileId,
                CompilationSourcePaths = input.CompilationSourceFiles,
                RuntimeSourcePaths = input.SourceFiles,
                ExpectedDependencyLock = plan.DependencyGraph,
                SingleFile = false
            });
            Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
            var host = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var native = await ObserveExitedConsole(host, fixture.RootPath,
                new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath }, string.Empty);
            Assert.Equal(0, native.Code);
            Assert.Empty(native.Error);
            Assert.Equal(native, await ObserveExitedConsole(build.ArtifactPath!, fixture.RootPath, Array.Empty<string>(), string.Empty));
        }
    }

    [PinnedSemanticHostFact]
    public async Task HybridModuleLoadsEncodedModuleFilesOnBothPinnedHosts()
    {
        const string source = """
            . "$PSScriptRoot/café.ps1"
            Export-ModuleMember -Function Get-Value
            """;
        using var fixture = OracleFixture.Create(source);
        var modulePath = Path.ChangeExtension(fixture.ScriptPath, ".psm1");
        File.WriteAllText(modulePath, source, Encoding.Latin1);
        var suffix = new byte[] { 0xe9 };
        var legacyCodePage = CodePagesEncodingProvider.Instance.GetEncoding(0) ?? Encoding.UTF8;
        var legacyName = "caf" + legacyCodePage.GetString(suffix) + ".ps1";
        var modernName = "caf" + Encoding.UTF8.GetString(suffix) + ".ps1";
        File.WriteAllText(Path.Combine(fixture.RootPath, modernName), "function Get-Value { return 20 }");
        if (!string.Equals(legacyName, modernName, StringComparison.OrdinalIgnoreCase))
            File.WriteAllText(Path.Combine(fixture.RootPath, legacyName), "function Get-Value { return 10 }");

        var observer = Path.Combine(fixture.RootPath, "ObserveModule.ps1");
        File.WriteAllText(observer, "Import-Module -Name $args[0] -ErrorAction Stop; Get-Value");
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var input = new PowerShellCompilationInputResolver(pin.ProfileId).Resolve(modulePath,
                PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid);
            var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                input.SourcePath, Path.Combine(fixture.RootPath, "hybrid"), "EncodedHybrid",
                input.Kind, input.Mode, allowUnreviewedDependencyResolution: true)
            {
                TargetFramework = pin.ProfileId == PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId ? "net472" : "net10.0",
                SemanticProfileId = pin.ProfileId,
                CompilationSourcePaths = input.CompilationSourceFiles,
                RuntimeSourcePaths = input.SourceFiles
            });
            Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
            Assert.Equal(1, build.Manifest!.UnitDispositionLedger!.EmittedUnits);
            var host = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var native = await ObserveExitedConsole(host, fixture.RootPath,
                new[] { "-NoProfile", "-NonInteractive", "-File", observer, modulePath }, string.Empty);
            var compiled = await ObserveExitedConsole(host, fixture.RootPath,
                new[] { "-NoProfile", "-NonInteractive", "-File", observer, build.ArtifactPath! }, string.Empty);
            Assert.Equal(0, native.Code);
            Assert.Empty(native.Error);
            Assert.Equal(native, compiled);
        }
    }

    [PinnedSemanticHostFact]
    public async Task HybridManifestRetainsEncodedSwitchAlternativesOnBothPinnedHosts()
    {
        const string source = """
            class EncodedModuleBase {}
            $path = switch ($PSVersionTable.PSVersion.Major) {
                { $_ -ge 7 } { 'café.ps1' }
                default { 'Desktop.ps1' }
            }
            . (Join-Path $PSScriptRoot $path)
            function Get-CompiledCount { return 21 }
            Export-ModuleMember -Function Get-ProfileValue, Get-CompiledCount
            """;
        const string desktopScript = "class DesktopValue : EncodedModuleBase {}\n" +
            "function Get-ProfileValue { . \"$PSScriptRoot/Call-Helper.ps1\"; 'desktop'; [int][char]'é' }";
        const string modernScript = "class ModernValue : EncodedModuleBase {}\n" +
            "function Get-ProfileValue { . \"$PSScriptRoot/Call-Helper.ps1\"; 'modern'; [int][char]'é' }";
        const string helperScript = "'helper'; [int][char]'é'";
        const string profile = PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId;
        using var fixture = OracleFixture.Create(source);
        var moduleRoot = Path.Combine(fixture.RootPath, "authored");
        var referenceRoot = Path.Combine(fixture.RootPath, "reference");
        var legacy = CodePagesEncodingProvider.Instance.GetEncoding(0) ?? Encoding.UTF8;
        var modernName = legacy.GetString(Encoding.Latin1.GetBytes("café.ps1"));
        var manifest = "@{ RootModule = 'Module.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @('Get-*'); FileList = @('Desktop.ps1', '" +
            System.Management.Automation.Language.CodeGeneration.EscapeSingleQuotedStringContent(modernName) + "', 'Call-Helper.ps1', 'Example.ps1') }";
        foreach (var (name, text) in new[]
                 {
                     ("Module.psm1", source), ("Desktop.ps1", desktopScript), (modernName, modernScript), ("Call-Helper.ps1", helperScript)
                 })
        {
            Directory.CreateDirectory(moduleRoot);
            Directory.CreateDirectory(referenceRoot);
            var bytes = Encoding.Latin1.GetBytes(text);
            File.WriteAllBytes(Path.Combine(moduleRoot, name), bytes);
            File.WriteAllText(Path.Combine(referenceRoot, name), legacy.GetString(bytes), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        var exampleBytes = Encoding.Latin1.GetBytes("# Sample file: café");
        File.WriteAllBytes(Path.Combine(moduleRoot, "Example.ps1"), exampleBytes);
        File.WriteAllBytes(Path.Combine(referenceRoot, "Example.ps1"), exampleBytes);
        File.WriteAllText(Path.Combine(moduleRoot, "Module.psd1"), manifest, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.WriteAllText(Path.Combine(referenceRoot, "Module.psd1"), manifest, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var observer = Path.Combine(fixture.RootPath, "ObserveManifest.ps1");
        File.WriteAllText(observer, "Import-Module -Name $args[0] -ErrorAction Stop; Get-ProfileValue; Get-CompiledCount");
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var target = pin.ProfileId == profile ? "net472" : "net10.0";
            var input = new PowerShellCompilationInputResolver(profile).Resolve(moduleRoot,
                PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
                allowDynamicModuleRuntimeSources: true);
            Assert.Single(input.CompilationSourceFiles);
            Assert.Contains(Path.Combine(moduleRoot, "Call-Helper.ps1"), input.SourceFiles);
            var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                input.SourcePath, Path.Combine(fixture.RootPath, target), "EncodedHybridManifest",
                input.Kind, input.Mode, allowUnreviewedDependencyResolution: true)
            {
                ModuleManifestPath = input.ModuleManifestPath,
                TargetFramework = target,
                SemanticProfileId = profile,
                ResourceMode = PowerShellCompilationResourceMode.CompleteModule,
                CompilationSourcePaths = input.CompilationSourceFiles,
                RuntimeSourcePaths = input.SourceFiles
            });
            Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
            Assert.Equal(1, build.Manifest!.UnitDispositionLedger!.EmittedUnits);
            var retainedFile = Assert.Single(build.Manifest.Files, file => Path.GetFileName(file.Path) == modernName);
            var needsRewrite = pin.ProfileId == profile || legacy.CodePage != Encoding.UTF8.CodePage;
            Assert.Equal(needsRewrite ? "GeneratedModuleDependency" : "ModuleDependency", retainedFile.Role);
            var helper = Assert.Single(build.Manifest.Files, file => Path.GetFileName(file.Path) == "Call-Helper.ps1");
            Assert.Equal(retainedFile.Role, helper.Role);
            var publishedDirectory = Path.GetDirectoryName(build.ArtifactPath!)!;
            if (needsRewrite)
                Assert.True(File.ReadAllBytes(Path.Combine(publishedDirectory, modernName)).AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
            var example = Assert.Single(build.Manifest.Files, file => Path.GetFileName(file.Path) == "Example.ps1");
            Assert.Equal("ModuleDependency", example.Role);
            Assert.Equal(exampleBytes, File.ReadAllBytes(Path.Combine(publishedDirectory, "Example.ps1")));
            var host = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var native = await ObserveExitedConsole(host, fixture.RootPath,
                new[] { "-NoProfile", "-NonInteractive", "-File", observer, Path.Combine(referenceRoot, "Module.psd1") }, string.Empty);
            var compiled = await ObserveExitedConsole(host, fixture.RootPath,
                new[] { "-NoProfile", "-NonInteractive", "-File", observer, build.ArtifactPath! }, string.Empty);
            Assert.Equal(0, native.Code);
            Assert.Empty(native.Error);
            Assert.Equal(native, compiled);
        }
    }
}
