using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Xunit;

namespace PowerForge.Tests;

public sealed class PowerForgeBundlePostProcessServiceTests
{
    [Fact]
    public void Run_AppliesArchiveDeleteAndMetadataRules()
    {
        var root = CreateTempRoot();
        try
        {
            var bundleRoot = Directory.CreateDirectory(Path.Combine(root, "bundle")).FullName;
            File.WriteAllText(Path.Combine(bundleRoot, "App.exe"), "app");
            File.WriteAllText(Path.Combine(bundleRoot, "createdump.exe"), "diag");
            File.WriteAllText(Path.Combine(bundleRoot, "notes.pdb"), "symbols");

            var pluginOne = Directory.CreateDirectory(Path.Combine(bundleRoot, "plugins", "Plugin.One")).FullName;
            var pluginTwo = Directory.CreateDirectory(Path.Combine(bundleRoot, "plugins", "Plugin.Two")).FullName;
            File.WriteAllText(Path.Combine(pluginOne, "plugin.dll"), "one");
            File.WriteAllText(Path.Combine(pluginTwo, "plugin.dll"), "two");

            var request = new PowerForgeBundlePostProcessRequest
            {
                ProjectRoot = root,
                BundleRoot = bundleRoot,
                BundleId = "portable",
                TargetName = "chat-app",
                Runtime = "win-x64",
                Framework = "net10.0-windows",
                Style = "PortableCompat",
                Configuration = "Release",
                Tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["primaryExecutable"] = "App.exe"
                },
                PostProcess = new DotNetPublishBundlePostProcessOptions
                {
                    ArchiveDirectories = new[]
                    {
                        new DotNetPublishBundleArchiveRule
                        {
                            Path = "plugins",
                            Mode = DotNetPublishBundleArchiveMode.ChildDirectories,
                            ArchiveNameTemplate = "{name}.ix-plugin.zip",
                            DeleteSource = true
                        }
                    },
                    DeletePatterns = new[] { "**/*.pdb", "**/createdump.exe" },
                    Metadata = new DotNetPublishBundleMetadataOptions
                    {
                        Path = "portable-bundle.json",
                        Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["primaryExecutable"] = "{primaryExecutable}",
                            ["bundleName"] = "{bundleId}"
                        }
                    }
                }
            };

            var result = new PowerForgeBundlePostProcessService(new NullLogger()).Run(request);

            Assert.Equal(bundleRoot, result.BundleRoot);
            Assert.Equal(2, result.ArchivePaths.Length);
            Assert.Contains(result.DeletedPaths, path => path.EndsWith("createdump.exe", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.DeletedPaths, path => path.EndsWith("notes.pdb", StringComparison.OrdinalIgnoreCase));

            var pluginZipOne = Path.Combine(bundleRoot, "plugins", "Plugin.One.ix-plugin.zip");
            var pluginZipTwo = Path.Combine(bundleRoot, "plugins", "Plugin.Two.ix-plugin.zip");
            Assert.True(File.Exists(pluginZipOne));
            Assert.True(File.Exists(pluginZipTwo));
            Assert.False(Directory.Exists(Path.Combine(bundleRoot, "plugins", "Plugin.One")));
            Assert.False(Directory.Exists(Path.Combine(bundleRoot, "plugins", "Plugin.Two")));
            Assert.False(File.Exists(Path.Combine(bundleRoot, "createdump.exe")));
            Assert.False(File.Exists(Path.Combine(bundleRoot, "notes.pdb")));

            using (var archive = ZipFile.OpenRead(pluginZipOne))
            {
                Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith("plugin.dll", StringComparison.OrdinalIgnoreCase));
            }

            Assert.NotNull(result.MetadataPath);
            Assert.True(File.Exists(result.MetadataPath!));
            var metadata = File.ReadAllText(result.MetadataPath!);
            Assert.Contains("\"bundleId\": \"portable\"", metadata, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"primaryExecutable\": \"App.exe\"", metadata, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"bundleName\": \"portable\"", metadata, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Run_RejectsMetadataPathsOutsideBundleRoot()
    {
        var root = CreateTempRoot();
        try
        {
            var bundleRoot = Directory.CreateDirectory(Path.Combine(root, "bundle")).FullName;
            File.WriteAllText(Path.Combine(bundleRoot, "App.exe"), "app");

            var request = new PowerForgeBundlePostProcessRequest
            {
                ProjectRoot = root,
                BundleRoot = bundleRoot,
                BundleId = "portable",
                PostProcess = new DotNetPublishBundlePostProcessOptions
                {
                    Metadata = new DotNetPublishBundleMetadataOptions
                    {
                        Path = "../outside.json"
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => new PowerForgeBundlePostProcessService(new NullLogger()).Run(request));
            Assert.Contains("metadata path", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
