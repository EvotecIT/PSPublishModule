using System.IO.Compression;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineScriptExecutionSeamTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollectModuleReleaseAssets_RejectsSplitScriptLayout(bool externalModule)
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        string scriptRoot = Path.Combine(root, "script");
        string externalRoot = Path.Combine(root, "external");
        string archiveRoot = Path.Combine(root, "release");
        try
        {
            Directory.CreateDirectory(scriptRoot);
            Directory.CreateDirectory(externalRoot);
            File.WriteAllText(Path.Combine(scriptRoot, "Invoke-Sample.ps1"), "'sample'");
            File.WriteAllText(Path.Combine(externalRoot, "payload.txt"), "external payload");
            ArtefactModuleEntry[] modules = externalModule
                ? [new ArtefactModuleEntry("External", false, "1.0.0", externalRoot)]
                : Array.Empty<ArtefactModuleEntry>();
            ArtefactCopyEntry[] copiedItems = externalModule
                ? Array.Empty<ArtefactCopyEntry>()
                : [new ArtefactCopyEntry(externalRoot, externalRoot, isDirectory: true)];
            var artefact = new ArtefactBuildResult(
                ArtefactType.Script,
                "release-script",
                scriptRoot,
                modules,
                copiedItems,
                Array.Empty<string>(),
                "Invoke-Sample.ps1");
            var method = typeof(ModulePipelineRunner).GetMethod(
                "CollectModuleReleaseAssets",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

            var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                method!.Invoke(null, new object?[] { new[] { artefact }, "release-script", archiveRoot }));

            InvalidOperationException validation = Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Contains("split layout", validation.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("outside its output root", validation.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(archiveRoot));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void CollectModuleReleaseAssets_RejectsNonPortableScriptPackedArchive()
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string archivePath = Path.Combine(root, "Invoke-Sample.zip");
            using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("Invoke-Sample.ps1").Open()))
                    writer.Write("'sample'");
                using (var writer = new StreamWriter(archive.CreateEntry("../escape.txt").Open()))
                    writer.Write("unsafe");
            }

            var artefact = new ArtefactBuildResult(
                ArtefactType.ScriptPacked,
                "release-script",
                archivePath,
                Array.Empty<ArtefactModuleEntry>(),
                Array.Empty<ArtefactCopyEntry>(),
                Array.Empty<string>(),
                "Invoke-Sample.ps1");
            var method = typeof(ModulePipelineRunner).GetMethod(
                "CollectModuleReleaseAssets",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

            var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                method!.Invoke(null, new object?[] { new[] { artefact }, "release-script", Path.Combine(root, "release") }));

            InvalidOperationException validation = Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Contains("valid portable release payload", validation.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void CollectModuleReleaseAssets_ValidatesSynthesizedScriptArchive()
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        string archiveRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "tests"));
            File.WriteAllText(Path.Combine(root, "Invoke-Sample.ps1"), "'sample'");
            File.WriteAllText(Path.Combine(root, "tests", "Sample.csproj"), "<Project />");
            var artefact = new ArtefactBuildResult(
                ArtefactType.Script,
                "release-script",
                root,
                Array.Empty<ArtefactModuleEntry>(),
                Array.Empty<ArtefactCopyEntry>(),
                Array.Empty<string>(),
                "Invoke-Sample.ps1");
            var method = typeof(ModulePipelineRunner).GetMethod(
                "CollectModuleReleaseAssets",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

            var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                method!.Invoke(null, new object?[] { new[] { artefact }, "release-script", archiveRoot }));

            InvalidOperationException validation = Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Contains("valid portable release payload", validation.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("repository or source-project content", validation.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(archiveRoot, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(ArtefactType.Packed)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void CollectModuleReleaseAssets_RejectsSynthesizedScriptArchiveCollisionWithSelectedOutput(
        ArtefactType packedType)
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            string scriptRoot = Directory.CreateDirectory(Path.Combine(root, "script")).FullName;
            string scriptPath = Path.Combine(scriptRoot, "Shared.ps1");
            File.WriteAllText(scriptPath, "'script'");
            string archiveRoot = Directory.CreateDirectory(Path.Combine(root, "release", "modules")).FullName;
            string packedPath = Path.Combine(archiveRoot, "Shared.zip");
            File.WriteAllText(packedPath, "packed-sentinel");
            var script = new ArtefactBuildResult(
                ArtefactType.Script,
                "shared",
                scriptRoot,
                Array.Empty<ArtefactModuleEntry>(),
                Array.Empty<ArtefactCopyEntry>(),
                Array.Empty<string>(),
                "Shared.ps1");
            var packed = new ArtefactBuildResult(
                packedType,
                "shared",
                packedPath,
                Array.Empty<ArtefactModuleEntry>(),
                Array.Empty<ArtefactCopyEntry>(),
                Array.Empty<string>(),
                packedType == ArtefactType.ScriptPacked ? "Shared.ps1" : null);
            var method = typeof(ModulePipelineRunner).GetMethod(
                "CollectModuleReleaseAssets",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

            Assert.NotNull(method);
            var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                method!.Invoke(null, new object?[] { new[] { script, packed }, "shared", archiveRoot }));

            InvalidOperationException collision = Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Contains("overlaps selected artefact output", collision.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("packed-sentinel", File.ReadAllText(packedPath));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CollectModuleReleaseAssets_ArchivesCompleteNestedScriptLayoutAndSelectsEvidence(
        bool leadingShebang)
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        string scriptPath = Path.Combine(root, "app", "Invoke-Sample.ps1");
        string resourcePath = Path.Combine(root, "app", "Resources", "data.json");
        string helperPath = Path.Combine(root, "app", "bin", "helper");
        string evidencePath = Path.Combine(root, "PowerForge.ScriptEvidence.json");
        string archiveRoot = Path.Combine(Path.GetDirectoryName(root)!, Guid.NewGuid().ToString("N"), "modules");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(resourcePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(helperPath)!);
            File.WriteAllText(
                scriptPath,
                (leadingShebang ? "#!/usr/bin/env pwsh\n" : string.Empty) + "'complete layout'\n");
            File.WriteAllText(resourcePath, "{}");
            File.WriteAllText(helperPath, "#!/bin/sh\nexit 0\n");
            int? expectedHelperMode = null;
            if (!OperatingSystem.IsWindows())
            {
                const UnixFileMode helperMode =
                    UnixFileMode.UserRead |
                    UnixFileMode.UserWrite |
                    UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead |
                    UnixFileMode.GroupExecute;
                File.SetUnixFileMode(helperPath, helperMode);
                expectedHelperMode = (int)helperMode;
            }
            File.WriteAllText(evidencePath, "{}");
            var artefact = new ArtefactBuildResult(
                ArtefactType.Script,
                "release-script",
                root,
                Array.Empty<ArtefactModuleEntry>(),
                Array.Empty<ArtefactCopyEntry>(),
                new[] { evidencePath },
                "app/Invoke-Sample.ps1");
            var method = typeof(ModulePipelineRunner).GetMethod(
                "CollectModuleReleaseAssets",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

            Assert.NotNull(method);
            string[] assets = Assert.IsType<string[]>(method!.Invoke(
                null,
                new object?[] { new[] { artefact }, "release-script", archiveRoot }));

            string archivePath = Path.Combine(archiveRoot, "Invoke-Sample.zip");
            Assert.Equal(new[] { Path.GetFullPath(archivePath), Path.GetFullPath(evidencePath) }, assets);
            byte[] firstArchive = File.ReadAllBytes(archivePath);
            string[] repeatedAssets = Assert.IsType<string[]>(method.Invoke(
                null,
                new object?[] { new[] { artefact }, "release-script", archiveRoot }));
            Assert.Equal(assets, repeatedAssets);
            Assert.Equal(firstArchive, File.ReadAllBytes(archivePath));
            using var archive = ZipFile.OpenRead(archivePath);
            ZipArchiveEntry scriptEntry = Assert.Single(archive.Entries, entry => entry.FullName == "app/Invoke-Sample.ps1");
            Assert.Contains(archive.Entries, entry => entry.FullName == "app/Resources/data.json");
            ZipArchiveEntry helperEntry = Assert.Single(archive.Entries, entry => entry.FullName == "app/bin/helper");
            int mode = (scriptEntry.ExternalAttributes >> 16) & 0x1FF;
            Assert.Equal(leadingShebang ? 0x49 : 0, mode & 0x49);
            if (expectedHelperMode.HasValue)
                Assert.Equal(expectedHelperMode.Value, (helperEntry.ExternalAttributes >> 16) & 0xFFF);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(Path.GetDirectoryName(archiveRoot)!, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(ArtefactType.Script)]
    [InlineData(ArtefactType.Packed)]
    [InlineData(ArtefactType.ScriptPacked)]
    public void CollectModuleReleaseAssets_RejectsSelectedScriptWhoseRootContainsAnotherReleaseOutput(
        ArtefactType otherType)
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        string archiveRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "first"));
            Directory.CreateDirectory(Path.Combine(root, "second"));
            File.WriteAllText(Path.Combine(root, "first", "First.ps1"), "'first'");
            File.WriteAllText(Path.Combine(root, "second", "Second.ps1"), "'second'");
            var selected = new ArtefactBuildResult(
                ArtefactType.Script,
                "first",
                root,
                Array.Empty<ArtefactModuleEntry>(),
                Array.Empty<ArtefactCopyEntry>(),
                Array.Empty<string>(),
                "first/First.ps1");
            string otherOutput = otherType == ArtefactType.Script
                ? root
                : Path.Combine(root, "unselected.zip");
            if (otherType != ArtefactType.Script)
                File.WriteAllText(otherOutput, "unselected artefact");
            var unselected = new ArtefactBuildResult(
                otherType,
                "second",
                otherOutput,
                Array.Empty<ArtefactModuleEntry>(),
                Array.Empty<ArtefactCopyEntry>(),
                Array.Empty<string>(),
                otherType is ArtefactType.Script or ArtefactType.ScriptPacked
                    ? "second/Second.ps1"
                    : null);
            var method = typeof(ModulePipelineRunner).GetMethod(
                "CollectModuleReleaseAssets",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

            var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                method!.Invoke(null, new object?[] { new[] { selected, unselected }, "first", archiveRoot }));

            InvalidOperationException validation = Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Contains("share output root", validation.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(archiveRoot));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(archiveRoot, recursive: true); } catch { }
        }
    }
}
