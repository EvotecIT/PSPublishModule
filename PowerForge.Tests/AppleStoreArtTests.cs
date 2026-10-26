using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using ImageMagick;
using PowerForge.Web;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class AppleStoreArtTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "powerforge-store-art-tests-" + Guid.NewGuid().ToString("N"));
    public AppleStoreArtTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void LineageBindsRawInventoryAndBothExactFiles()
    {
        WritePng("raw.png", 12, 16); WritePng("art.png", 24, 32);
        var raw = Input("raw.png", 12, 16); var output = Lineage(raw);
        AppleStoreArtLineageValidator.Validate([output], [raw], root);
        // The inventory remains unchanged while an actual retained input changes.
        WritePng("raw.png", 12, 16, MagickColors.Red);
        Assert.Throws<InvalidOperationException>(() => AppleStoreArtLineageValidator.Validate([output], [raw], root));
        WritePng("raw.png", 12, 16);
        WritePng("art.png", 24, 32, MagickColors.Blue);
        Assert.Throws<InvalidOperationException>(() => AppleStoreArtLineageValidator.Validate([output], [raw], root));
    }

    [Fact]
    public void LineageRejectsUninventoriedInputsAndUnsafePaths()
    {
        WritePng("raw.png", 12, 16); WritePng("art.png", 24, 32);
        var raw = Input("raw.png", 12, 16); var output = Lineage(raw);
        Assert.Throws<InvalidOperationException>(() => AppleStoreArtLineageValidator.Validate([output], [], root));
        foreach (var path in new[] { "../raw.png", "/raw.png", "C:/raw.png", "a/../raw.png", "a\\raw.png" })
        {
            output.Inputs = [new AppleStoreArtInput { Path = path, Sha256 = raw.Sha256, Width = 12, Height = 16 }];
            Assert.Throws<InvalidOperationException>(() => AppleStoreArtLineageValidator.ValidateMetadata(output));
        }
    }

    [Theory]
    [InlineData("white")]
    [InlineData("lime")]
    [InlineData("blue")]
    public void PerceptualHashIsStableAndDistinguishesMirroredGradients(string color)
    {
        using var gradient = new MagickImage("gradient:black-" + color, 90, 80);
        gradient.Rotate(90); gradient.Write(Path.Combine(root, "first.png"), MagickFormat.Png24);
        gradient.Flop(); gradient.Write(Path.Combine(root, "second.png"), MagickFormat.Png24);
        var first = AppleStoreArtComposer.PerceptualHash(Path.Combine(root, "first.png"));
        Assert.Equal(first, AppleStoreArtComposer.PerceptualHash(Path.Combine(root, "first.png")));
        Assert.NotEqual(first, AppleStoreArtComposer.PerceptualHash(Path.Combine(root, "second.png")));
        Assert.Matches("^dhash-v1:[0-9a-f]{16}$", first);
    }

    [Fact]
    public async Task ComposerRejectsOccupiedOutputWithoutChangingFiles()
    {
        var output = Path.Combine(root, "output"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "retain.txt"), "existing work");
        var config = Path.Combine(root, "store-art.json"); File.WriteAllText(config, "{\"captureRoot\":\".\"}");
        await Assert.ThrowsAsync<IOException>(() => new AppleStoreArtComposer().ComposeAsync(config, output));
        Assert.Equal("existing work", File.ReadAllText(Path.Combine(output, "retain.txt")));
    }

    [Fact]
    public async Task ComposerRejectsCaptureRootEscapeAndDuplicateFrameIdsBeforeRendering()
    {
        var config = Path.Combine(root, "store-art.json");
        File.WriteAllText(config, "{\"captureRoot\":\".\"}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AppleStoreArtComposer().ComposeAsync(config, Path.Combine(root, "..", "outside")));
        File.WriteAllText(config, """{"frames":[{"id":"home"},{"id":"HOME"}]}""");
        await Assert.ThrowsAsync<ArgumentException>(() => new AppleStoreArtComposer().ComposeAsync(config, Path.Combine(root, "output")));
        Assert.False(Directory.Exists(Path.Combine(root, "output")));
    }

    [Fact]
    public async Task WatchIsByteExactAndHasLineageAndContactSheetWithoutBrowser()
    {
        WritePng("watch.png", 416, 496);
        var config = Path.Combine(root, "store-art.json");
        File.WriteAllText(config, """{"devices":["watch"],"frames":[{"id":"home","layout":"watch","capture":"watch.png"}]}""");
        File.WriteAllText(Path.Combine(root, "store-captions.en-US.json"), "{}");
        var output = Path.Combine(root, "output");
        var result = Assert.Single(await new AppleStoreArtComposer().ComposeAsync(config, output));
        Assert.Equal(File.ReadAllBytes(Path.Combine(root, "watch.png")), File.ReadAllBytes(Path.Combine(root, result.Path)));
        Assert.Equal("passthrough", result.Renderer);
        Assert.True(File.Exists(Path.Combine(output, "_review", "en-US-watch.png")));
        AppleStoreArtLineageValidator.Validate([result], [Input("watch.png", 416, 496)], root);
    }

    [Theory]
    [InlineData("ar", "headlineKey", "المنزل", "Noto Sans Arabic")]
    [InlineData("he", "eyebrowKey", "בית", "Noto Sans Hebrew")]
    [InlineData("hi", "sublineKey", "घर", "Noto Sans Devanagari")]
    [InlineData("th", "headlineKey", "บ้าน", "Noto Sans Thai")]
    [InlineData("en-US", "headlineKey", "Home البيت", "Noto Sans Arabic")]
    [InlineData("en-US", "sublineKey", "Zażółć gęślą jaźń. Français Ελληνικά Русский 日本語 한국어 繁體中文 😀", "U+1F600")]
    public async Task UnsupportedCaptionGlyphFailsBeforeBrowserAndPublishesNothing(string locale, string field, string text, string missing)
    {
        WritePng("raw.png", 12, 16);
        var config = Path.Combine(root, "store-art.json");
        File.WriteAllText(config, JsonSerializer.Serialize(new
        {
            locales = new[] { locale }, devices = new[] { "iphone-69" },
            frames = new[] { new Dictionary<string, string>
            {
                ["id"] = "home", ["layout"] = "hero", ["capture"] = "raw.png", ["headlineKey"] = "title", [field] = "unsupported"
            } }
        }));
        File.WriteAllText(Path.Combine(root, $"store-captions.{locale}.json"),
            JsonSerializer.Serialize(new { title = "Home", unsupported = text }));
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => new AppleStoreArtComposer()
            .ComposeAsync(config, Path.Combine(root, "output"), browserExecutablePath: Path.Combine(root, "no-browser")));
        Assert.Contains($"locale '{locale}'", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("frame 'home'", failure.Message);
        Assert.Contains("missing offline font", failure.Message);
        Assert.Contains(missing, failure.Message);
        Assert.False(Directory.Exists(Path.Combine(root, "output")));
        Assert.Empty(Directory.GetDirectories(root, "*.staging-*"));
    }

    [Theory]
    [InlineData("'Arial',sans-serif")]
    [InlineData("var(--brand-font)")]
    public async Task FontTokenCannotUseAnUnbundledSystemFont(string display)
    {
        WritePng("raw.png", 12, 16);
        var config = Path.Combine(root, "store-art.json");
        File.WriteAllText(config, JsonSerializer.Serialize(new
        {
            theme = new { name = "casaray", tokens = new Dictionary<string, string>
                { ["--font-display"] = display, ["--brand-font"] = "'Arial',sans-serif" } },
            devices = new[] { "iphone-69" },
            frames = new[] { new { id = "home", layout = "hero", capture = "raw.png", headlineKey = "title" } }
        }));
        File.WriteAllText(Path.Combine(root, "store-captions.en-US.json"), """{"title":"Home"}""");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => new AppleStoreArtComposer()
            .ComposeAsync(config, Path.Combine(root, "output"), browserExecutablePath: Path.Combine(root, "no-browser")));
        Assert.Contains("Locale 'en-US', frame 'home': missing offline font 'Arial'", failure.Message);
        Assert.False(Directory.Exists(Path.Combine(root, "output")));
    }

    [Theory]
    [InlineData("var(--font-body)", "var(--font-body)")]
    [InlineData("VAR(--font-body)", "var(--font-body)")]
    [InlineData("var(--brand-font)", "var(--font-body)")]
    [InlineData("var(--missing, var(--font-body))", "var(--font-body)")]
    [InlineData("var(--font-body, var(--missing))", "var(--font-body)")]
    [InlineData("var(--missing, 'Inter','Noto Sans CJK',sans-serif)", "var(--font-body)")]
    [InlineData("var(--brand-font, var(--font-body))", "var(--optional-brand-font)")]
    [InlineData("var(--brand-font, var(--font-body))", "initial")]
    public async Task FontAliasesAndFallbacksReachGlyphValidation(string display, string brand)
    {
        WritePng("raw.png", 12, 16);
        var config = Path.Combine(root, "store-art.json");
        File.WriteAllText(config, JsonSerializer.Serialize(new
        {
            theme = new { name = "casaray", tokens = new Dictionary<string, string>
            {
                ["--font-display"] = display, ["--brand-font"] = brand, ["--font-eyebrow"] = "var(--font-display)"
            } },
            devices = new[] { "iphone-69" },
            frames = new[] { new { id = "home", layout = "hero", capture = "raw.png", headlineKey = "title" } }
        }));
        File.WriteAllText(Path.Combine(root, "store-captions.en-US.json"), """{"title":"Home البيت"}""");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => new AppleStoreArtComposer()
            .ComposeAsync(config, Path.Combine(root, "output"), browserExecutablePath: Path.Combine(root, "no-browser")));
        Assert.Contains("Locale 'en-US', frame 'home': missing offline font 'Noto Sans Arabic'", failure.Message);
        Assert.False(Directory.Exists(Path.Combine(root, "output")));
    }

    [Fact]
    public async Task ComposeCliJsonUsesTheSameCamelCaseLineageContractAsTheArtifact()
    {
        WritePng("watch.png", 416, 496);
        var config = Path.Combine(root, "store-art.json");
        File.WriteAllText(config, """{"devices":["watch"],"frames":[{"id":"home","layout":"watch","capture":"watch.png"}]}""");
        File.WriteAllText(Path.Combine(root, "store-captions.en-US.json"), "{}");
        var output = Path.Combine(root, "output");
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = root
        };
        foreach (var argument in new[] { typeof(PowerForge.Web.Cli.WebPipelineRunner).Assembly.Location,
            "apple-store-art", "compose", "--config", config, "--out", output, "--output", "json" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            var text = await stdout;
            Assert.True(process.ExitCode == 0, (await stderr) + text);
            using var json = JsonDocument.Parse(text);
            var frame = Assert.Single(json.RootElement.GetProperty("result").GetProperty("frames").EnumerateArray());
            using var artifact = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "store-art.lineage.json")));
            var expected = artifact.RootElement[0];
            foreach (var name in new[] { "path", "sha256", "perceptualHash", "locale" })
                Assert.Equal(expected.GetProperty(name).GetString(), frame.GetProperty(name).GetString());
            Assert.False(frame.TryGetProperty("Path", out _));
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }

    [Fact]
    public async Task WatchRemovesOnlyUnusedAlphaAndPreservesEveryRgbSample()
    {
        using (var image = new MagickImage(MagickColors.Orange, 416, 496)) image.Write(Path.Combine(root, "watch.png"), MagickFormat.Png32);
        var raw = File.ReadAllBytes(Path.Combine(root, "watch.png"));
        var config = Path.Combine(root, "store-art.json");
        File.WriteAllText(config, """{"devices":["watch"],"frames":[{"id":"home","layout":"watch","capture":"watch.png"}]}""");
        File.WriteAllText(Path.Combine(root, "store-captions.en-US.json"), "{}");
        var result = Assert.Single(await new AppleStoreArtComposer().ComposeAsync(config, Path.Combine(root, "output")));
        using var expected = new MagickImage(raw);
        using var actual = new MagickImage(Path.Combine(root, result.Path));
        Assert.False(actual.HasAlpha);
        Assert.Equal(expected.ToByteArray(MagickFormat.Rgb), actual.ToByteArray(MagickFormat.Rgb));
        Assert.Equal(raw, File.ReadAllBytes(Path.Combine(root, "watch.png")));
    }

    [Fact]
    public async Task TrustedRootAllowsParentAliasesButRejectsLinkedCaptureEntries()
    {
        if (OperatingSystem.IsWindows()) return; // Creating links requires a separate Windows privilege.
        var selected = Directory.CreateDirectory(Path.Combine(root, "physical", "selected"));
        var alias = Path.Combine(root, "alias"); Directory.CreateSymbolicLink(alias, Path.Combine(root, "physical"));
        try
        {
            using (var image = new MagickImage(MagickColors.White, 416, 496)) image.Write(Path.Combine(selected.FullName, "watch.png"), MagickFormat.Png24);
            var config = Path.Combine(root, "store-art.json");
            File.WriteAllText(config, """{"captureRoot":"alias/selected","devices":["watch"],"frames":[{"id":"home","layout":"watch","capture":"watch.png"}]}""");
            File.WriteAllText(Path.Combine(root, "store-captions.en-US.json"), "{}");
            Assert.Single(await new AppleStoreArtComposer().ComposeAsync(config, Path.Combine(alias, "selected", "output")));
            File.CreateSymbolicLink(Path.Combine(selected.FullName, "linked.png"), Path.Combine(selected.FullName, "watch.png"));
            File.WriteAllText(config, File.ReadAllText(config).Replace("watch.png", "linked.png"));
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => new AppleStoreArtComposer().ComposeAsync(config, Path.Combine(alias, "selected", "rejected")));
            Assert.Contains("links", failure.Message);
        }
        finally { Directory.Delete(alias); }
    }

    [Fact]
    public async Task WatchRejectsAlphaInsteadOfChangingCaptureBytes()
    {
        using (var image = new MagickImage(MagickColors.Transparent, 416, 496)) image.Write(Path.Combine(root, "watch.png"), MagickFormat.Png32);
        var before = File.ReadAllBytes(Path.Combine(root, "watch.png"));
        var config = Path.Combine(root, "store-art.json");
        File.WriteAllText(config, """{"devices":["watch"],"frames":[{"id":"home","layout":"watch","capture":"watch.png"}]}""");
        File.WriteAllText(Path.Combine(root, "store-captions.en-US.json"), "{}");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => new AppleStoreArtComposer().ComposeAsync(config, Path.Combine(root, "output")));
        Assert.Contains("no alpha", failure.Message);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(root, "watch.png")));
        Assert.False(Directory.Exists(Path.Combine(root, "output")));
        Assert.Empty(Directory.GetDirectories(root, "*.staging-*"));
    }

    private void WritePng(string name, uint width, uint height, IMagickColor<byte>? color = null)
    {
        using var image = new MagickImage(color ?? MagickColors.White, width, height);
        image.Write(Path.Combine(root, name), MagickFormat.Png24);
    }
    private string Hash(string name) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, name)))).ToLowerInvariant();
    private AppleStoreArtInput Input(string name, int width, int height) => new() { Path = name, Sha256 = Hash(name), Width = width, Height = height };
    private AppleStoreArtLineage Lineage(AppleStoreArtInput raw) => new()
    {
        Path = "art.png", Sha256 = Hash("art.png"), Width = 24, Height = 32, Frame = "home", Locale = "en-US", Device = "iphone-69", Theme = "casaray",
        SpecSha256 = new string('a', 64), CaptionsSha256 = new string('b', 64), TemplateSha256 = new string('c', 64), RenderSha256 = new string('d', 64),
        Renderer = "test", PerceptualHash = AppleStoreArtComposer.PerceptualHash(Path.Combine(root, "art.png")), Inputs = [raw]
    };
}
