using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed class ScoopManifestPreparationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "powerforge-scoop-" + Guid.NewGuid().ToString("N"));
    private readonly PowerForgeReleaseSpec _spec = new()
    {
        GitHub = new PowerForgeReleaseGitHubOptions { Owner = "Example", Repository = "Tools", TagTemplate = "Studio-v{Version}" },
        Scoop = new PowerForgeReleaseScoopOptions
        {
            Name = "fixture-studio", Description = "Local document editor", Homepage = "https://example.test/", License = "MIT",
            Executable = "Studio.exe", ShortcutName = "Fixture Studio", Target = "Studio.Windows",
            Runtimes = ["win-x64", "win-arm64"],
            UrlTemplate = "https://example.test/Studio-v$version/studio-$version-{Runtime}.zip"
        }
    };
    private readonly List<PowerForgeReleaseAssetEntry> _assets = [];
    private string Manifest => Path.Combine(_root, "release-manifest.json");
    private string Checksums => Path.Combine(_root, "SHA256SUMS.txt");
    private string Output => Path.Combine(_root, "output");

    public ScoopManifestPreparationServiceTests()
    {
        Directory.CreateDirectory(_root);
        foreach (var runtime in new[] { "win-x64", "win-arm64" })
        {
            var name = "studio-1.2.3-" + runtime + ".zip";
            using (var zip = ZipFile.Open(Path.Combine(_root, name), ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("Studio.exe").Open()))
                writer.Write("fixture application " + runtime);
            _assets.Add(new PowerForgeReleaseAssetEntry
            {
                Category = PowerForgeReleaseAssetCategory.Tool, Target = "Studio.Windows", Runtime = runtime, Version = "1.2.3",
                Path = "C:\\old-builder\\" + name, RelativeStagePath = "GitHub/" + name
            });
        }
        WriteMetadata();
    }

    [Fact]
    public void Prepare_WritesVerifiedArchitecturesShortcutAndStableApplicationUpdates()
    {
        var path = Prepare();
        Assert.Equal(Path.Combine(Output, "fixture-studio.json"), path);
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var root = json.RootElement;
        Assert.Equal("1.2.3", root.GetProperty("version").GetString());
        foreach (var (architecture, runtime) in new[] { ("64bit", "win-x64"), ("arm64", "win-arm64") })
        {
            var archive = "studio-1.2.3-" + runtime + ".zip";
            Assert.Equal(Hash(Path.Combine(_root, archive)), root.GetProperty("architecture").GetProperty(architecture).GetProperty("hash").GetString());
            Assert.Equal("https://example.test/Studio-v1.2.3/" + archive,
                root.GetProperty("architecture").GetProperty(architecture).GetProperty("url").GetString());
            Assert.Equal("https://example.test/Studio-v$version/studio-$version-" + runtime + ".zip",
                root.GetProperty("autoupdate").GetProperty("architecture").GetProperty(architecture).GetProperty("url").GetString());
        }
        Assert.Equal("Studio.exe", root.GetProperty("shortcuts")[0][0].GetString());
        Assert.Equal("Fixture Studio", root.GetProperty("shortcuts")[0][1].GetString());
        Assert.Equal("https://api.github.com/repos/Example/Tools/releases?per_page=100", root.GetProperty("checkver").GetProperty("url").GetString());
        Assert.Equal("$[?(@.draft == false && @.prerelease == false && @.tag_name =~ /^Studio-v[0-9]+\\.[0-9]+\\.[0-9]+$/)].tag_name",
            root.GetProperty("checkver").GetProperty("jsonpath").GetString());
        Assert.Equal("Studio-v([0-9]+\\.[0-9]+\\.[0-9]+)", root.GetProperty("checkver").GetProperty("regex").GetString());
    }

    [Theory]
    [InlineData("archive")]
    [InlineData("manifest")]
    public void Prepare_RejectsChangedReleaseBytesBeforeWritingOutput(string changed)
    {
        File.AppendAllText(changed == "manifest" ? Manifest : Path.Combine(_root, "studio-1.2.3-win-x64.zip"), "tampered");
        var error = Assert.Throws<InvalidOperationException>(() => Prepare());
        Assert.Contains("checksum", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Output));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("version")]
    public void Prepare_RejectsAmbiguousMissingOrMixedVersionArchives(string change)
    {
        if (change == "duplicate") _assets.Add(_assets[0]);
        else if (change == "missing") _assets.RemoveAt(0);
        else _assets[1].Version = "1.2.4";
        WriteMetadata();
        var error = Assert.Throws<InvalidOperationException>(() => Prepare());
        Assert.Contains(change == "version" ? "matching" : "exactly one", error.Message);
        Assert.False(Directory.Exists(Output));
    }

    [Fact]
    public void Prepare_RejectsArchiveWithoutConfiguredExecutable()
    {
        _spec.Scoop!.Executable = "Missing.exe";
        var error = Assert.Throws<InvalidOperationException>(() => Prepare());
        Assert.Contains("missing", error.Message);
        Assert.False(Directory.Exists(Output));
    }

    [Fact]
    public void Prepare_PreservesExistingOutput()
    {
        Directory.CreateDirectory(Output);
        var sentinel = Path.Combine(Output, "keep.txt");
        File.WriteAllText(sentinel, "existing output");
        Assert.Contains("already exists", Assert.Throws<InvalidOperationException>(() => Prepare()).Message);
        Assert.Equal("existing output", File.ReadAllText(sentinel));
    }

    private string Prepare() => new ScoopManifestPreparationService().Prepare(_spec, Manifest, Checksums, _root, Output);

    private void WriteMetadata()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        File.WriteAllText(Manifest, JsonSerializer.Serialize(new { schemaVersion = 1, assetEntries = _assets }, options));
        File.WriteAllLines(Checksums, Directory.GetFiles(_root).Where(path => path != Checksums)
            .Select(path => Hash(path) + " *" + Path.GetFileName(path)));
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    public void Dispose() => Directory.Delete(_root, true);
}
