using System.Management.Automation;
using PowerForge;

public sealed class ModuleBinaryPayloadLayoutTests
{
    [Fact]
    public void ResolveBuildPayloads_PreservesDesktopAndEveryModernFramework()
    {
        var payloads = ModuleBinaryPayloadLayout.ResolveBuildPayloads(new[] { "net472", "net10.0", "net8.0" });

        Assert.Collection(
            payloads,
            payload =>
            {
                Assert.Equal("net472", payload.Framework);
                Assert.Equal(ModuleBinaryPayloadKind.Default, payload.Kind);
                Assert.Equal("Default", payload.FolderName);
            },
            payload =>
            {
                Assert.Equal("net10.0", payload.Framework);
                Assert.Equal(ModuleBinaryPayloadKind.Core, payload.Kind);
                Assert.Equal("Core-net10.0", payload.FolderName);
            },
            payload =>
            {
                Assert.Equal("net8.0", payload.Framework);
                Assert.Equal(ModuleBinaryPayloadKind.Core, payload.Kind);
                Assert.Equal("Core", payload.FolderName);
            });
    }

    [Fact]
    public void ResolveBuildPayloads_RejectsMultipleDesktopPayloadsUntilRuntimeSelectionIsDefined()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ModuleBinaryPayloadLayout.ResolveBuildPayloads(new[] { "net472", "net48" }));

        Assert.Contains("Multiple Default target frameworks", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("net8.0", "net10.0-windows")]
    [InlineData("net10.0", "net10.0-windows")]
    [InlineData("net8.0-windows", "net10.0-windows")]
    [InlineData("netstandard2.0", "net10.0-windows")]
    public void ResolveBuildPayloads_RejectsSideBySidePlatformQualifiedCorePayloads(
        string first,
        string second)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ModuleBinaryPayloadLayout.ResolveBuildPayloads(new[] { first, second }));

        Assert.Contains("platform-qualified Core target frameworks", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveBuildPayloads_AllowsSinglePlatformQualifiedCorePayload()
    {
        var payload = Assert.Single(ModuleBinaryPayloadLayout.ResolveBuildPayloads(new[] { "net10.0-windows" }));

        Assert.Equal(ModuleBinaryPayloadKind.Core, payload.Kind);
        Assert.Equal("Core", payload.FolderName);
    }

    [Fact]
    public void BinaryConflictAnalysis_IsOptInForDirectAndPipelineBuilds()
    {
        Assert.False(new ModuleBuilder.Options().EmitBinaryConflictOwnerNotes);
        Assert.False(new ModuleBuildSpec().AnalyzeInstalledBinaryConflictsDuringBuild);
    }

    [Theory]
    [InlineData(8, "Core")]
    [InlineData(9, "Core")]
    [InlineData(10, "Core-net10.0")]
    [InlineData(11, "Core-net10.0")]
    public void ResolveRuntimePayloadFolder_SelectsHighestCompatibleModernPayload(int runtimeMajor, string expectedFolder)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var libRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Lib"));
        Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Core"));
        Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Core-net10.0"));
        Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Default"));
        Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Standard"));
        File.WriteAllText(
            Path.Combine(libRoot.FullName, "Core", ModuleBinaryPayloadLayout.TargetFrameworkMarkerFileName),
            "net8.0");

        try
        {
            var selected = ModuleBinaryPayloadLayout.ResolveRuntimePayloadFolder(
                libRoot.FullName,
                "Core",
                new Version(runtimeMajor, 0));

            Assert.Equal(expectedFolder, selected);
            Assert.Equal(
                "Default",
                ModuleBinaryPayloadLayout.ResolveRuntimePayloadFolder(libRoot.FullName, "Desktop", new Version(4, 8)));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ResolveRuntimePayloadFolder_RanksMarkedCoreBaselineWithNamedPayloads()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var libRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Lib"));
        var core = Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Core"));
        Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Core-net8.0"));
        File.WriteAllText(
            Path.Combine(core.FullName, ModuleBinaryPayloadLayout.TargetFrameworkMarkerFileName),
            "net10.0");

        try
        {
            Assert.Equal(
                "Core",
                ModuleBinaryPayloadLayout.ResolveRuntimePayloadFolder(
                    libRoot.FullName,
                    "Core",
                    new Version(10, 0)));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData(null, 8, "Standard")]
    [InlineData("net10.0", 8, "Standard")]
    [InlineData("net8.0", 8, "Core")]
    public void ResolveRuntimePayloadFolder_UsesCoreMetadataBeforeStandardFallback(
        string? coreFramework,
        int runtimeMajor,
        string expectedFolder)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var libRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Lib"));
        Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Core"));
        Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Standard"));
        if (coreFramework is not null)
        {
            File.WriteAllText(
                Path.Combine(libRoot.FullName, "Core", ModuleBinaryPayloadLayout.TargetFrameworkMarkerFileName),
                coreFramework);
        }

        try
        {
            var selected = ModuleBinaryPayloadLayout.ResolveRuntimePayloadFolder(
                libRoot.FullName,
                "Core",
                new Version(runtimeMajor, 0));

            Assert.Equal(expectedFolder, selected);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData(8, "Standard")]
    [InlineData(10, "Core-net10.0")]
    public void ResolveRuntimePayloadFolder_UsesNamedCoreBeforeStandardFallback(
        int runtimeMajor,
        string expectedFolder)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var libRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Lib"));
        Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Core-net10.0"));
        Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Standard"));

        try
        {
            var selected = ModuleBinaryPayloadLayout.ResolveRuntimePayloadFolder(
                libRoot.FullName,
                "Core",
                new Version(runtimeMajor, 0));

            Assert.Equal(expectedFolder, selected);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ResolveAssemblyLoadContextTargetDirectories_ReturnsEveryModernPayload()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var libRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Lib"));
        var core = Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Core"));
        var net10 = Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Core-net10.0"));
        Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Default"));
        Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Core-backup"));
        Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Standard-backup"));
        Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Default-backup"));

        try
        {
            var directories = ModuleBinaryPayloadLayout.ResolveAssemblyLoadContextTargetDirectories(libRoot.FullName);

            Assert.Equal(new[] { core.FullName, net10.FullName }, directories);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ResolveValidationPayloadDirectories_ReturnsEverySelectableCorePayload()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var libRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Lib"));
        var core = Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Core"));
        var net10 = Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Core-net10.0"));
        var standard = Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Standard"));
        var desktop = Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Default"));

        try
        {
            Assert.Equal(
                new[] { core.FullName, net10.FullName, standard.FullName },
                ModuleBinaryPayloadLayout.ResolveValidationPayloadDirectories(libRoot.FullName, "Core"));
            Assert.Equal(
                new[] { desktop.FullName },
                ModuleBinaryPayloadLayout.ResolveValidationPayloadDirectories(libRoot.FullName, "Desktop"));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void BuildPowerShellRuntimeSelector_DiscoversCompatiblePackagedPayloads()
    {
        var selector = ModuleBinaryPayloadLayout.BuildPowerShellRuntimeSelector();

        Assert.Contains("$PowerForgeRuntimeVersion = [Environment]::Version", selector);
        Assert.Contains("foreach ($PowerForgeRuntimeFolder in @($AssemblyFolders.Name))", selector);
        Assert.Contains("^Core-(?:net|netcoreapp)", selector);
        Assert.Contains("$Framework = $PowerForgeSelectedRuntimeFolder", selector);
        Assert.Contains("PowerForge.TargetFramework.txt", selector);
        Assert.Contains("$PowerForgeCoreBaselineVersion -le $PowerForgeRuntimeVersion", selector);
        Assert.Contains("} elseif ($Standard) {", selector);
        Assert.DoesNotContain("Core-net10.0", selector, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, false, "Standard")]
    [InlineData("net8.0", false, "Core")]
    [InlineData(null, true, "Core-net10.0")]
    public void BuildPowerShellRuntimeSelector_SelectsCompatiblePayload(
        string? coreFramework,
        bool includeNamedCurrentRuntime,
        string expectedFolder)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var libRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Lib"));
        Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Core"));
        Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Standard"));
        if (coreFramework is not null)
        {
            File.WriteAllText(
                Path.Combine(libRoot.FullName, "Core", ModuleBinaryPayloadLayout.TargetFrameworkMarkerFileName),
                coreFramework);
        }
        if (includeNamedCurrentRuntime)
            Directory.CreateDirectory(Path.Combine(libRoot.FullName, $"Core-net{Environment.Version.Major}.0"));

        try
        {
            using var powerShell = PowerShell.Create();
            powerShell.AddScript(
                "param($LibRoot)\n" +
                "$AssemblyFolders = @(Get-ChildItem -LiteralPath $LibRoot -Directory)\n" +
                "$Core = Test-Path -LiteralPath ([IO.Path]::Combine($LibRoot, 'Core'))\n" +
                "$Standard = Test-Path -LiteralPath ([IO.Path]::Combine($LibRoot, 'Standard'))\n" +
                "$Framework = 'Standard'\n" +
                ModuleBinaryPayloadLayout.BuildPowerShellRuntimeSelector() +
                "$Framework\n");
            powerShell.AddArgument(libRoot.FullName);

            var result = powerShell.Invoke();

            Assert.Empty(powerShell.Streams.Error);
            Assert.Equal(expectedFolder, Assert.Single(result).BaseObject);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void BuildPowerShellRuntimeSelector_RanksMarkedCoreBaselineWithNamedPayloads()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var libRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Lib"));
        var core = Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Core"));
        Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Core-net8.0"));
        File.WriteAllText(
            Path.Combine(core.FullName, ModuleBinaryPayloadLayout.TargetFrameworkMarkerFileName),
            $"net{Environment.Version.Major}.0");

        try
        {
            using var powerShell = PowerShell.Create();
            powerShell.AddScript(
                "param($LibRoot)\n" +
                "$AssemblyFolders = @(Get-ChildItem -LiteralPath $LibRoot -Directory)\n" +
                "$Core = $true\n" +
                "$Standard = $false\n" +
                "$Framework = 'Core'\n" +
                ModuleBinaryPayloadLayout.BuildPowerShellRuntimeSelector() +
                "$Framework\n");
            powerShell.AddArgument(libRoot.FullName);

            var result = powerShell.Invoke();

            Assert.Empty(powerShell.Streams.Error);
            Assert.Equal("Core", Assert.Single(result).BaseObject);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
