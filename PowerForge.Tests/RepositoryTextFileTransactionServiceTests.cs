using System.Text;
using PowerForge;

namespace PowerForge.Tests;

public sealed class RepositoryTextFileTransactionServiceTests
{
    [Fact]
    public void Apply_replaces_every_file_after_preparation_succeeds()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var first = WriteFile(root, "first.txt", "old-first");
            var second = WriteFile(root, "second.txt", "old-second");

            new RepositoryTextFileTransactionService().Apply(new[]
            {
                new RepositoryTextFileUpdate(first, "old-first", "new-first"),
                new RepositoryTextFileUpdate(second, "old-second", "new-second")
            });

            Assert.Equal("new-first", File.ReadAllText(first));
            Assert.Equal("new-second", File.ReadAllText(second));
            Assert.Empty(Directory.EnumerateFiles(root.FullName, "*.powerforge-*"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Apply_rolls_back_prior_replacements_when_a_later_replacement_fails()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var first = WriteFile(root, "first.txt", "old-first");
            var second = WriteFile(root, "second.txt", "old-second");
            var replacementIndex = 0;
            var service = new RepositoryTextFileTransactionService((source, destination, backup) =>
            {
                if (replacementIndex++ == 1)
                    throw new IOException("Injected second replacement failure.");

                File.Replace(source, destination, backup);
            });

            var exception = Assert.Throws<InvalidOperationException>(() => service.Apply(new[]
            {
                new RepositoryTextFileUpdate(first, "old-first", "new-first"),
                new RepositoryTextFileUpdate(second, "old-second", "new-second")
            }));

            Assert.Contains("were rolled back", exception.Message, StringComparison.Ordinal);
            Assert.Equal("old-first", File.ReadAllText(first));
            Assert.Equal("old-second", File.ReadAllText(second));
            Assert.Empty(Directory.EnumerateFiles(root.FullName, "*.powerforge-*"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Apply_detects_a_file_changed_after_planning_before_any_replacement()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var first = WriteFile(root, "first.txt", "old-first");
            var second = WriteFile(root, "second.txt", "changed-after-plan");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new RepositoryTextFileTransactionService().Apply(new[]
                {
                    new RepositoryTextFileUpdate(first, "old-first", "new-first"),
                    new RepositoryTextFileUpdate(second, "old-second", "new-second")
                }));

            Assert.Contains("changed after version planning", exception.Message, StringComparison.Ordinal);
            Assert.Equal("old-first", File.ReadAllText(first));
            Assert.Equal("changed-after-plan", File.ReadAllText(second));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Apply_preserves_unix_file_mode()
    {
        if (OperatingSystem.IsWindows())
            return;

        var root = CreateTemporaryDirectory();

        try
        {
            var path = WriteFile(root, "tool.sh", "old");
            var expectedMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            File.SetUnixFileMode(path, expectedMode);

            new RepositoryTextFileTransactionService().Apply(new[]
            {
                new RepositoryTextFileUpdate(path, "old", "new")
            });

            Assert.Equal(expectedMode, File.GetUnixFileMode(path));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8-bom")]
    [InlineData("utf16-le")]
    [InlineData("utf16-be")]
    [InlineData("utf32-le")]
    [InlineData("utf32-be")]
    public void Apply_preserves_text_encoding_and_bom(string format)
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var path = Path.Combine(root.FullName, "version.txt");
            var encoding = CreateEncoding(format);
            WriteEncodedFile(path, "Example.Tool@1.2.3", encoding);
            var expectedPreamble = encoding.GetPreamble();

            new RepositoryTextFileTransactionService().Apply(new[]
            {
                new RepositoryTextFileUpdate(path, "Example.Tool@1.2.3", "Example.Tool@1.2.4")
            });

            var bytes = File.ReadAllBytes(path);
            Assert.Equal(expectedPreamble, bytes.Take(expectedPreamble.Length));
            Assert.Equal(
                "Example.Tool@1.2.4",
                encoding.GetString(bytes, expectedPreamble.Length, bytes.Length - expectedPreamble.Length));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static Encoding CreateEncoding(string format)
        => format switch
        {
            "utf8" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            "utf8-bom" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            "utf16-le" => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
            "utf16-be" => new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
            "utf32-le" => new UTF32Encoding(bigEndian: false, byteOrderMark: true),
            "utf32-be" => new UTF32Encoding(bigEndian: true, byteOrderMark: true),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown encoding fixture.")
        };

    private static void WriteEncodedFile(string path, string content, Encoding encoding)
    {
        var preamble = encoding.GetPreamble();
        var contentBytes = encoding.GetBytes(content);
        var bytes = new byte[preamble.Length + contentBytes.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(contentBytes, 0, bytes, preamble.Length, contentBytes.Length);
        File.WriteAllBytes(path, bytes);
    }

    private static DirectoryInfo CreateTemporaryDirectory()
        => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-file-transaction-" + Guid.NewGuid().ToString("N")));

    private static string WriteFile(DirectoryInfo root, string fileName, string content)
    {
        var path = Path.Combine(root.FullName, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private static void TryDelete(DirectoryInfo directory)
    {
        try { directory.Delete(recursive: true); } catch { }
    }
}
