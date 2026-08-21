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
    public void ResolveAssemblyLoadContextTargetDirectories_ReturnsEveryModernPayload()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var libRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Lib"));
        var core = Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Core"));
        var net10 = Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Core-net10.0"));
        Directory.CreateDirectory(Path.Combine(libRoot.FullName, "Default"));

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
    public void BuildPowerShellRuntimeSelector_EmitsDescendingCompatibleRuntimeChecks()
    {
        var selector = ModuleBinaryPayloadLayout.BuildPowerShellRuntimeSelector(new[] { "net8.0", "net10.0", "net12.0" });

        Assert.Contains("$PowerForgeRuntimeVersion = [Environment]::Version", selector);
        Assert.Contains("[Version]'12.0'", selector);
        Assert.Contains("$Framework = 'Core-net12.0'", selector);
        Assert.Contains("[Version]'10.0'", selector);
        Assert.Contains("$Framework = 'Core-net10.0'", selector);
        Assert.True(
            selector.IndexOf("[Version]'12.0'", StringComparison.Ordinal) <
            selector.IndexOf("[Version]'10.0'", StringComparison.Ordinal));
    }
}
