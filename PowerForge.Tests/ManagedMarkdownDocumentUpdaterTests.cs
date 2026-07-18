namespace PowerForge.Tests;

public sealed class ManagedMarkdownDocumentUpdaterTests
{
    [Fact]
    public void ValidateBlock_PreservesTwoArgumentPublicSignature()
    {
        var method = typeof(ManagedMarkdownDocumentUpdater).GetMethod(
            nameof(ManagedMarkdownDocumentUpdater.ValidateBlock),
            new[] { typeof(string), typeof(string) });

        Assert.NotNull(method);
    }

    [Fact]
    public void Update_CreatesManagedDocumentWhenExplicitlyAllowed()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "SPONSORS.md");

        var result = new ManagedMarkdownDocumentUpdater().Update(new ManagedMarkdownUpdateRequest
        {
            Path = path,
            BlockId = "sponsors",
            Markdown = "Generated roster",
            CreateIfMissing = true,
            NewDocumentTitle = "Sponsors"
        });

        Assert.True(result.Changed);
        Assert.True(result.Created);
        Assert.False(result.Appended);
        Assert.Equal("# Sponsors\n\n<!-- POWERFORGE:sponsors:START -->\nGenerated roster\n<!-- POWERFORGE:sponsors:END -->\n", Normalize(File.ReadAllText(path)));
    }

    [Fact]
    public void Update_AppendsOnlyWhenExplicitlyConfigured()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "README.md");
        File.WriteAllText(path, "# Project\n\nExisting content.\n");

        var result = new ManagedMarkdownDocumentUpdater().Update(new ManagedMarkdownUpdateRequest
        {
            Path = path,
            BlockId = "sponsors",
            Markdown = "Generated roster",
            MissingBlockBehavior = ManagedMarkdownMissingBlockBehavior.Append
        });

        Assert.True(result.Appended);
        var text = Normalize(File.ReadAllText(path));
        Assert.StartsWith("# Project\n\nExisting content.\n\n", text, StringComparison.Ordinal);
        Assert.Contains("<!-- POWERFORGE:sponsors:START -->", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_RejectsDuplicateMarkersWithoutChangingDocument()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "README.md");
        var original = "<!-- POWERFORGE:sponsors:START -->\none\n<!-- POWERFORGE:sponsors:START -->\ntwo\n<!-- POWERFORGE:sponsors:END -->\n";
        File.WriteAllText(path, original);

        var exception = Assert.Throws<InvalidOperationException>(() => new ManagedMarkdownDocumentUpdater().Update(new ManagedMarkdownUpdateRequest
        {
            Path = path,
            BlockId = "sponsors",
            Markdown = "replacement"
        }));

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void Update_DoesNotMatchDifferentMarkerNamespaceWithSameBlockId()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "README.md");
        const string original = "<!-- BENCHMARK:sponsors:START -->\nbenchmark data\n<!-- BENCHMARK:sponsors:END -->\n";
        File.WriteAllText(path, original);

        var exception = Assert.Throws<InvalidOperationException>(() => new ManagedMarkdownDocumentUpdater().Update(new ManagedMarkdownUpdateRequest
        {
            Path = path,
            BlockId = "sponsors",
            Markdown = "replacement"
        }));

        Assert.Contains("was not found", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void Update_PreservesCrlfAndLegacyBenchmarkMarkers()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "README.md");
        File.WriteAllText(path, "Before\r\n<!-- BENCHMARK:demo:START -->\r\nold\r\n<!-- BENCHMARK:demo:END -->\r\nAfter\r\n");

        var result = new BenchmarkDocumentUpdater().UpdateBlock(path, "demo", "new\nvalue");

        Assert.True(result.Changed);
        var bytes = File.ReadAllBytes(path);
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("\r\nnew\r\nvalue\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\nold\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_PreservesDocumentedTwoPartBenchmarkMarkers()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "README.md");
        const string original = "Before\n<!-- managed-module-benchmark-table:start -->\nold\n<!-- managed-module-benchmark-table:end -->\nAfter\n";
        File.WriteAllText(path, original);

        var updater = new BenchmarkDocumentUpdater();
        updater.ValidateBlock(path, "managed-module-benchmark-table");
        var result = updater.UpdateBlock(path, "managed-module-benchmark-table", "new\nvalue");

        Assert.True(result.Changed);
        Assert.Equal(
            "Before\n<!-- managed-module-benchmark-table:start -->\nnew\nvalue\n<!-- managed-module-benchmark-table:end -->\nAfter\n",
            Normalize(File.ReadAllText(path)));
    }

    [Fact]
    public void ValidateUpdate_RejectsExistingDirectoryEvenWhenCreationIsAllowed()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "SPONSORS.md");
        Directory.CreateDirectory(path);

        var exception = Assert.Throws<IOException>(() => new ManagedMarkdownDocumentUpdater().ValidateUpdate(new ManagedMarkdownUpdateRequest
        {
            Path = path,
            BlockId = "sponsors",
            Markdown = "replacement",
            CreateIfMissing = true
        }));

        Assert.Contains("existing directory", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateMany_ProjectsDisjointBlocksBeforeWritingFinalDocument()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "README.md");
        File.WriteAllText(
            path,
            "<!-- POWERFORGE:sponsors:START -->\nold sponsors\n<!-- POWERFORGE:sponsors:END -->\n" +
            "Between\n" +
            "<!-- POWERFORGE:stats:START -->\nold stats\n<!-- POWERFORGE:stats:END -->\n");

        var results = new ManagedMarkdownDocumentUpdater().UpdateMany(new[]
        {
            new ManagedMarkdownUpdateRequest { Path = path, BlockId = "sponsors", Markdown = "new sponsors" },
            new ManagedMarkdownUpdateRequest { Path = path, BlockId = "stats", Markdown = "new stats" }
        });

        Assert.Equal(2, results.Length);
        Assert.All(results, result => Assert.True(result.Changed));
        var text = Normalize(File.ReadAllText(path));
        Assert.Contains("<!-- POWERFORGE:sponsors:START -->\nnew sponsors\n<!-- POWERFORGE:sponsors:END -->", text, StringComparison.Ordinal);
        Assert.Contains("<!-- POWERFORGE:stats:START -->\nnew stats\n<!-- POWERFORGE:stats:END -->", text, StringComparison.Ordinal);
        Assert.DoesNotContain("old sponsors", text, StringComparison.Ordinal);
        Assert.DoesNotContain("old stats", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateMany_CoalescesFileSymlinkAliasBeforeWritingFinalDocument()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "README.md");
        var alias = Path.Combine(root, "README-alias.md");
        File.WriteAllText(
            path,
            "<!-- POWERFORGE:sponsors:START -->\nold sponsors\n<!-- POWERFORGE:sponsors:END -->\n" +
            "<!-- POWERFORGE:stats:START -->\nold stats\n<!-- POWERFORGE:stats:END -->\n");
        try
        {
            File.CreateSymbolicLink(alias, path);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or NotSupportedException or PlatformNotSupportedException)
        {
            return;
        }

        var results = new ManagedMarkdownDocumentUpdater().UpdateMany(new[]
        {
            new ManagedMarkdownUpdateRequest { Path = path, BlockId = "sponsors", Markdown = "new sponsors" },
            new ManagedMarkdownUpdateRequest { Path = alias, BlockId = "stats", Markdown = "new stats" }
        });

        Assert.Equal(2, results.Length);
        var text = File.ReadAllText(path);
        Assert.Contains("new sponsors", text, StringComparison.Ordinal);
        Assert.Contains("new stats", text, StringComparison.Ordinal);
        Assert.DoesNotContain("old sponsors", text, StringComparison.Ordinal);
        Assert.DoesNotContain("old stats", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateMany_CoalescesHardLinkAliasBeforeWritingFinalDocument()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "README.md");
        var alias = Path.Combine(root, "README-hardlink.md");
        File.WriteAllText(
            path,
            "<!-- POWERFORGE:sponsors:START -->\nold sponsors\n<!-- POWERFORGE:sponsors:END -->\n" +
            "<!-- POWERFORGE:stats:START -->\nold stats\n<!-- POWERFORGE:stats:END -->\n");
        CreateHardLink(alias, path);

        var results = new ManagedMarkdownDocumentUpdater().UpdateMany(new[]
        {
            new ManagedMarkdownUpdateRequest { Path = path, BlockId = "sponsors", Markdown = "old sponsors" },
            new ManagedMarkdownUpdateRequest { Path = alias, BlockId = "stats", Markdown = "new stats" }
        });

        Assert.Equal(2, results.Length);
        Assert.All(results, result => Assert.True(result.Changed));
        var text = File.ReadAllText(path);
        Assert.Contains("old sponsors", text, StringComparison.Ordinal);
        Assert.Contains("new stats", text, StringComparison.Ordinal);
        Assert.DoesNotContain("old stats", text, StringComparison.Ordinal);
        Assert.Equal(text, File.ReadAllText(alias));
    }

    [Fact]
    public void WindowsFileIdentity_PreservesAll128IdentifierBits()
    {
        var first = ExistingFilePathIdentityResolver.FormatWindowsFileIdentity(1, 2, 3);
        var differentUpperHalf = ExistingFilePathIdentityResolver.FormatWindowsFileIdentity(1, 2, 4);

        Assert.NotEqual(first, differentUpperHalf);
    }

    [Theory]
    [InlineData("NTFS", true)]
    [InlineData("FAT32", true)]
    [InlineData("ReFS", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LegacyWindowsFileIdentity_RejectsReFsAndUnknownFileSystems(string? fileSystemName, bool expected)
    {
        Assert.Equal(
            expected,
            ExistingFilePathIdentityResolver.IsLegacyWindowsFileIdentitySafe(
                fileSystemName,
                volumeInformationApiUnavailable: false));
    }

    [Fact]
    public void LegacyWindowsFileIdentity_AllowsSystemsThatPredateReFs()
    {
        Assert.True(ExistingFilePathIdentityResolver.IsLegacyWindowsFileIdentitySafe(
            fileSystemName: null,
            volumeInformationApiUnavailable: true));
    }

    [Fact]
    public void UpdateMany_RejectsDuplicateLogicalTargetWithoutWriting()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "README.md");
        const string original = "<!-- POWERFORGE:sponsors:START -->\nold\n<!-- POWERFORGE:sponsors:END -->\n";
        File.WriteAllText(path, original);

        var exception = Assert.Throws<InvalidOperationException>(() => new ManagedMarkdownDocumentUpdater().UpdateMany(new[]
        {
            new ManagedMarkdownUpdateRequest { Path = path, BlockId = "sponsors", Markdown = "new" },
            new ManagedMarkdownUpdateRequest { Path = path, BlockId = "sponsors", Markdown = "old" }
        }));

        Assert.Contains("targeted more than once", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void UpdateMany_KeepsCaseDistinctFilesSeparateOnCaseSensitiveFileSystem()
    {
        var root = CreateTempRoot();
        var upperPath = Path.Combine(root, "A.md");
        var lowerPath = Path.Combine(root, "a.md");
        const string upperOriginal = "<!-- POWERFORGE:sponsors:START -->\nupper old\n<!-- POWERFORGE:sponsors:END -->\n";
        const string lowerOriginal = "<!-- POWERFORGE:sponsors:START -->\nlower old\n<!-- POWERFORGE:sponsors:END -->\n";
        File.WriteAllText(upperPath, upperOriginal);
        File.WriteAllText(lowerPath, lowerOriginal);
        if (string.Equals(File.ReadAllText(upperPath), File.ReadAllText(lowerPath), StringComparison.Ordinal))
        {
            var exception = Assert.Throws<InvalidOperationException>(() => new ManagedMarkdownDocumentUpdater().UpdateMany(new[]
            {
                new ManagedMarkdownUpdateRequest { Path = upperPath, BlockId = "sponsors", Markdown = "upper new" },
                new ManagedMarkdownUpdateRequest { Path = lowerPath, BlockId = "sponsors", Markdown = "lower new" }
            }));
            Assert.Contains("targeted more than once", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("lower old", File.ReadAllText(upperPath), StringComparison.Ordinal);
            return;
        }

        var results = new ManagedMarkdownDocumentUpdater().UpdateMany(new[]
        {
            new ManagedMarkdownUpdateRequest { Path = upperPath, BlockId = "sponsors", Markdown = "upper new" },
            new ManagedMarkdownUpdateRequest { Path = lowerPath, BlockId = "sponsors", Markdown = "lower new" }
        });

        Assert.Equal(2, results.Length);
        Assert.Contains("upper new", File.ReadAllText(upperPath), StringComparison.Ordinal);
        Assert.DoesNotContain("lower new", File.ReadAllText(upperPath), StringComparison.Ordinal);
        Assert.Contains("lower new", File.ReadAllText(lowerPath), StringComparison.Ordinal);
        Assert.DoesNotContain("upper new", File.ReadAllText(lowerPath), StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateMany_CoalescesWindowsDriveRootCasingAliases()
    {
        if (Path.DirectorySeparatorChar != '\\')
            return;

        var root = CreateTempRoot();
        var path = Path.Combine(root, "README.md");
        const string original =
            "<!-- POWERFORGE:sponsors:START -->\nold sponsors\n<!-- POWERFORGE:sponsors:END -->\n" +
            "<!-- POWERFORGE:stats:START -->\nold stats\n<!-- POWERFORGE:stats:END -->\n";
        File.WriteAllText(path, original);
        var alias = char.ToLowerInvariant(path[0]) + path.Substring(1);

        var results = new ManagedMarkdownDocumentUpdater().UpdateMany(new[]
        {
            new ManagedMarkdownUpdateRequest { Path = path, BlockId = "sponsors", Markdown = "new sponsors" },
            new ManagedMarkdownUpdateRequest { Path = alias, BlockId = "stats", Markdown = "new stats" }
        });

        Assert.Equal(2, results.Length);
        var text = File.ReadAllText(path);
        Assert.Contains("new sponsors", text, StringComparison.Ordinal);
        Assert.Contains("new stats", text, StringComparison.Ordinal);
        Assert.DoesNotContain("old sponsors", text, StringComparison.Ordinal);
        Assert.DoesNotContain("old stats", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UpdateMany_RejectsReplacementThatIntroducesRequestedNestedBlockWithoutWriting(bool outerFirst)
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "README.md");
        const string original = "<!-- POWERFORGE:outer:START -->\nold\n<!-- POWERFORGE:outer:END -->\n";
        File.WriteAllText(path, original);
        var outer = new ManagedMarkdownUpdateRequest
        {
            Path = path,
            BlockId = "outer",
            Markdown = "before\n<!-- POWERFORGE:inner:START -->\nintroduced\n<!-- POWERFORGE:inner:END -->\nafter"
        };
        var inner = new ManagedMarkdownUpdateRequest
        {
            Path = path,
            BlockId = "inner",
            Markdown = "inner replacement",
            MissingBlockBehavior = ManagedMarkdownMissingBlockBehavior.Append
        };
        var requests = outerFirst ? new[] { outer, inner } : new[] { inner, outer };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ManagedMarkdownDocumentUpdater().UpdateMany(requests));

        Assert.True(
            exception.Message.Contains("overlap", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("duplic", StringComparison.OrdinalIgnoreCase),
            exception.Message);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void Update_PreservesUtf8BomAndEveryByteOutsideMixedEndingBlock()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "README.md");
        const string original = "Before\r\nKeep\n<!-- POWERFORGE:sponsors:START -->\r\nold\ncontent\r\n<!-- POWERFORGE:sponsors:END -->\nAfter\rTail";
        WriteUtf8Bom(path, original);

        var result = new ManagedMarkdownDocumentUpdater().Update(new ManagedMarkdownUpdateRequest
        {
            Path = path,
            BlockId = "sponsors",
            Markdown = "new\nvalue"
        });

        Assert.True(result.Changed);
        const string expected = "Before\r\nKeep\n<!-- POWERFORGE:sponsors:START -->\r\nnew\r\nvalue\r\n<!-- POWERFORGE:sponsors:END -->\nAfter\rTail";
        var expectedContent = System.Text.Encoding.UTF8.GetBytes(expected);
        var expectedBytes = new byte[3 + expectedContent.Length];
        expectedBytes[0] = 0xEF;
        expectedBytes[1] = 0xBB;
        expectedBytes[2] = 0xBF;
        Buffer.BlockCopy(expectedContent, 0, expectedBytes, 3, expectedContent.Length);
        Assert.Equal(expectedBytes, File.ReadAllBytes(path));
    }

    private static void WriteUtf8Bom(string path, string value)
    {
        var content = System.Text.Encoding.UTF8.GetBytes(value);
        var bytes = new byte[3 + content.Length];
        bytes[0] = 0xEF;
        bytes[1] = 0xBB;
        bytes[2] = 0xBF;
        Buffer.BlockCopy(content, 0, bytes, 3, content.Length);
        File.WriteAllBytes(path, bytes);
    }

    private static void CreateHardLink(string linkPath, string existingPath)
    {
        var succeeded = Path.DirectorySeparatorChar == '\\'
            ? CreateHardLinkWindows(linkPath, existingPath, IntPtr.Zero)
            : CreateHardLinkUnix(existingPath, linkPath) == 0;
        if (!succeeded)
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            throw new IOException($"Unable to create hard-link test artifact: {new System.ComponentModel.Win32Exception(error).Message}");
        }
    }

    [System.Runtime.InteropServices.DllImport(
        "kernel32.dll",
        EntryPoint = "CreateHardLinkW",
        CharSet = System.Runtime.InteropServices.CharSet.Unicode,
        SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CreateHardLinkWindows(string fileName, string existingFileName, IntPtr securityAttributes);

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int CreateHardLinkUnix(string existingPath, string newPath);

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Normalize(string value)
        => value.Replace("\r\n", "\n").Replace('\r', '\n');
}
