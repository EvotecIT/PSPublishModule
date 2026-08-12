using PowerForge;

namespace PowerForge.Tests;

public sealed class ProjectVersionBindingServiceTests
{
    [Fact]
    public void Apply_updates_configured_files_from_the_resolved_project_version()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var jsonPath = WriteFile(root, "tool.json", "{ \"command\": \"Example.Tool@1.2.3\" }");
            var readmePath = WriteFile(root, "README.md", "Run `Example.Tool@1.2.3`.");
            var bindings = new[]
            {
                CreateBinding("tool.json"),
                CreateBinding("README.md")
            };

            new ProjectVersionBindingService(new NullLogger()).Apply(
                root.FullName,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Example.Tool"] = "1.2.4"
                },
                bindings,
                whatIf: false);

            Assert.Contains("Example.Tool@1.2.4", File.ReadAllText(jsonPath), StringComparison.Ordinal);
            Assert.Contains("Example.Tool@1.2.4", File.ReadAllText(readmePath), StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Apply_validates_every_binding_before_writing_any_file()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var firstPath = WriteFile(root, "first.txt", "Example.Tool@1.2.3");
            WriteFile(root, "second.txt", "No version is present.");
            var bindings = new[]
            {
                CreateBinding("first.txt"),
                CreateBinding("second.txt")
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new ProjectVersionBindingService(new NullLogger()).Apply(
                    root.FullName,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Example.Tool"] = "1.2.4"
                    },
                    bindings,
                    whatIf: false));

            Assert.Contains("found 0 matches", exception.Message, StringComparison.Ordinal);
            Assert.Equal("Example.Tool@1.2.3", File.ReadAllText(firstPath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Apply_in_plan_mode_validates_without_writing()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var path = WriteFile(root, "tool.txt", "Example.Tool@1.2.3");

            new ProjectVersionBindingService(new NullLogger()).Apply(
                root.FullName,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Example.Tool"] = "1.2.4"
                },
                new[] { CreateBinding("tool.txt") },
                whatIf: true);

            Assert.Equal("Example.Tool@1.2.3", File.ReadAllText(path));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Apply_preserves_whitespace_that_is_part_of_the_pattern()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var path = WriteFile(root, "tool.txt", "preferred 1.2.3 version; compact=1.2.3");
            var binding = CreateBinding("tool.txt");
            binding.Pattern = @" 1\.2\.3 ";

            new ProjectVersionBindingService(new NullLogger()).Apply(
                root.FullName,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Example.Tool"] = "1.2.4"
                },
                new[] { binding },
                whatIf: false);

            Assert.Equal("preferred1.2.4version; compact=1.2.3", File.ReadAllText(path));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Apply_preserves_whitespace_in_the_replacement()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var path = WriteFile(root, "tool.txt", "Example.Tool@1.2.3");
            var binding = CreateBinding("tool.txt");
            binding.Replacement = " {Version} ";

            new ProjectVersionBindingService(new NullLogger()).Apply(
                root.FullName,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Example.Tool"] = "1.2.4"
                },
                new[] { binding },
                whatIf: false);

            Assert.Equal("Example.Tool@ 1.2.4 ", File.ReadAllText(path));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Apply_rejects_bomless_non_utf8_without_modifying_the_file()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var path = Path.Combine(root.FullName, "tool.txt");
            var originalBytes = new byte[]
            {
                (byte)'E', (byte)'x', (byte)'a', (byte)'m', (byte)'p', (byte)'l', (byte)'e', (byte)'.',
                (byte)'T', (byte)'o', (byte)'o', (byte)'l', (byte)'@', (byte)'1', (byte)'.', (byte)'2',
                (byte)'.', (byte)'3', (byte)' ', 0xE9
            };
            File.WriteAllBytes(path, originalBytes);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new ProjectVersionBindingService(new NullLogger()).Apply(
                    root.FullName,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Example.Tool"] = "1.2.4"
                    },
                    new[] { CreateBinding("tool.txt") },
                    whatIf: false));

            Assert.Contains("not valid UTF-8", exception.Message, StringComparison.Ordinal);
            Assert.Equal(originalBytes, File.ReadAllBytes(path));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Apply_rejects_paths_outside_the_repository()
    {
        var root = CreateTemporaryDirectory();
        var outside = CreateTemporaryDirectory();

        try
        {
            WriteFile(outside, "tool.txt", "Example.Tool@1.2.3");
            var relativePath = Path.GetRelativePath(root.FullName, Path.Combine(outside.FullName, "tool.txt"));

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new ProjectVersionBindingService(new NullLogger()).Apply(
                    root.FullName,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Example.Tool"] = "1.2.4"
                    },
                    new[] { CreateBinding(relativePath) },
                    whatIf: false));

            Assert.Contains("must resolve inside", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
            TryDelete(outside);
        }
    }

    [Fact]
    public void Apply_rejects_symbolic_links_that_escape_the_repository()
    {
        var root = CreateTemporaryDirectory();
        var outside = CreateTemporaryDirectory();

        try
        {
            var outsidePath = WriteFile(outside, "tool.txt", "Example.Tool@1.2.3");
            var linkPath = Path.Combine(root.FullName, "tool.txt");
            try
            {
                File.CreateSymbolicLink(linkPath, outsidePath);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new ProjectVersionBindingService(new NullLogger()).Apply(
                    root.FullName,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Example.Tool"] = "1.2.4"
                    },
                    new[] { CreateBinding("tool.txt") },
                    whatIf: false));

            Assert.Contains("symbolic link or junction", exception.Message, StringComparison.Ordinal);
            Assert.Equal("Example.Tool@1.2.3", File.ReadAllText(outsidePath));
        }
        finally
        {
            TryDelete(root);
            TryDelete(outside);
        }
    }

    private static ProjectVersionBinding CreateBinding(string path)
        => new()
        {
            Path = path,
            Project = "Example.Tool",
            Pattern = @"(?<=Example\.Tool@)\d+\.\d+\.\d+"
        };

    private static DirectoryInfo CreateTemporaryDirectory()
        => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-version-binding-" + Guid.NewGuid().ToString("N")));

    private static string WriteFile(DirectoryInfo root, string relativePath, string content)
    {
        var path = Path.Combine(root.FullName, relativePath);
        File.WriteAllText(path, content);
        return path;
    }

    private static void TryDelete(DirectoryInfo directory)
    {
        try { directory.Delete(recursive: true); } catch { }
    }
}
