namespace PowerForge.Tests;

public sealed class PowerForgeReleaseModuleArtifactLockTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitiallyLockedArtifactRequiresObservableChangeAfterUnlock(bool rewrite)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = Path.Combine(root, "module.zip");
            File.WriteAllText(path, "original");
            IReadOnlyDictionary<string, PowerForgeReleaseService.ModuleArtifactSnapshot> baseline;
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                baseline = PowerForgeReleaseService.CaptureModuleArtifactBaseline([root]);

            if (rewrite) File.WriteAllText(path, "new module build");
            var produced = PowerForgeReleaseService.ResolveProducedModuleArtifacts([root], baseline);

            Assert.Equal(rewrite ? new[] { path } : Array.Empty<string>(), produced);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LockedOutputIsIgnoredOnlyWhenItAlreadyExistedWithUnchangedMetadata(bool changed, bool newFile)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = Path.Combine(root, "module.zip");
            if (!newFile) File.WriteAllText(path, "original");
            var baseline = PowerForgeReleaseService.CaptureModuleArtifactBaseline([root]);
            if (changed) File.WriteAllText(path, "new module build");
            using var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);

            if (changed)
                Assert.Throws<IOException>(() => PowerForgeReleaseService.ResolveProducedModuleArtifacts([root], baseline));
            else
                Assert.Empty(PowerForgeReleaseService.ResolveProducedModuleArtifacts([root], baseline));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
