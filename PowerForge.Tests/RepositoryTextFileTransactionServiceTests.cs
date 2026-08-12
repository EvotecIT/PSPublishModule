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
