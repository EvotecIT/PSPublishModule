using System.Text;

namespace PowerForge.Tests;

public sealed class RepositoryTextFileEditorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "powerforge-editor-" + Guid.NewGuid().ToString("N"));
    public RepositoryTextFileEditorTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8bom")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("utf32le")]
    [InlineData("utf32be")]
    public void SavePreservesUnicodeBomAndLineEndings(string kind)
    {
        Encoding encoding = kind switch
        {
            "utf8bom" => new UTF8Encoding(true, true),
            "utf16le" => new UnicodeEncoding(false, true, true),
            "utf16be" => new UnicodeEncoding(true, true, true),
            "utf32le" => new UTF32Encoding(false, true, true),
            "utf32be" => new UTF32Encoding(true, true, true),
            _ => new UTF8Encoding(false, true)
        };
        var path = Path.Combine(_root, "Build.ps1");
        File.WriteAllText(path, "# Zażółć 🧪\r\nfirst\nlast", encoding);
        var editor = new RepositoryTextFileEditor();
        var original = editor.Open(path);
        var text = original.Text.Replace("first", "updated", StringComparison.Ordinal);
        var saved = editor.Save(original, text);
        Assert.Equal(text, saved.Text);
        Assert.Equal(encoding.GetPreamble().Concat(encoding.GetBytes(text)), File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(_root, "*.powerforge-*"));
    }

    [Fact]
    public void ExternalByteChangesIncludingBomOnlyChangesAreNotOverwritten()
    {
        var path = Path.Combine(_root, "config.json");
        File.WriteAllText(path, "original", new UTF8Encoding(false));
        var editor = new RepositoryTextFileEditor();
        var original = editor.Open(path);
        File.WriteAllText(path, "original", new UTF8Encoding(true));
        var changed = File.ReadAllBytes(path);
        Assert.Throws<IOException>(() => editor.Save(original, "draft"));
        Assert.Equal(changed, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(_root, "*.powerforge-*"));
    }

    [Fact]
    public void OversizedBinaryAndInvalidUnicodeCannotBecomeEditableDocuments()
    {
        var path = Path.Combine(_root, "sample");
        var editor = new RepositoryTextFileEditor();
        File.WriteAllBytes(path, new byte[RepositoryTextFileEditor.MaximumBytes + 1]);
        Assert.Throws<IOException>(() => editor.Open(path));
        File.WriteAllBytes(path, [1, 0, 2]);
        Assert.Throws<InvalidOperationException>(() => editor.Open(path));
        File.WriteAllBytes(path, [0xFF, 0xFE, 0x00]);
        Assert.Throws<InvalidOperationException>(() => editor.Open(path));
        File.WriteAllText(path, "original", new UTF32Encoding(false, true, true));
        var original = editor.Open(path);
        var bytes = File.ReadAllBytes(path);
        Assert.Throws<IOException>(() => editor.Save(original, new string('a', 70000)));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(_root, "*.powerforge-*"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacementRaceRetainsTheDisplacedExternalEditAndReportsItsRecoveryPath(bool oversized)
    {
        var path = Path.Combine(_root, "file.txt");
        File.WriteAllText(path, "original");
        var original = new RepositoryTextFileEditor().Open(path);
        var external = oversized ? new string('x', RepositoryTextFileEditor.MaximumBytes + 1) : "external edit racing replacement";
        var transaction = new RepositoryTextFileTransactionService((source, target, backup) =>
        {
            File.WriteAllText(target, external);
            File.Replace(source, target, backup);
        });
        var error = Assert.Throws<InvalidOperationException>(() => transaction.Apply([
            new RepositoryTextFileUpdate(path, original.Text, "draft", original.ContentHash, RepositoryTextFileEditor.MaximumBytes)
        ]));
        var recovery = Assert.Single(Directory.GetFiles(_root, "*.bak"));
        Assert.Equal(external, File.ReadAllText(recovery));
        Assert.Contains(recovery, error.Message);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }
}
