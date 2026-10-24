using System.Text.Json;
using ImageMagick;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class PowerForgeCliAppleReleaseTests
{
    [Fact]
    public async Task AppleScreenshots_ComposedApprovalRequiresRetainedRawInputsAndExactOutput()
    {
        var repoRoot = FindRepositoryRoot(); var root = CreateTempDirectory();
        try
        {
            var art = Directory.CreateDirectory(Path.Combine(root, "art"));
            var raw = Path.Combine(root, "raw.png"); var composed = Path.Combine(art.FullName, "01-home.png");
            using (var image = new MagickImage(MagickColors.White, 12, 16)) image.Write(raw, MagickFormat.Png24);
            using (var image = new MagickImage(MagickColors.Blue, 24, 32)) image.Write(composed, MagickFormat.Png24);
            string Hash(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            var input = new AppleStoreArtInput { Path = "raw.png", Sha256 = Hash(raw), Width = 12, Height = 16 };
            var lineage = new AppleStoreArtLineage
            {
                Path = "art/01-home.png", Sha256 = Hash(composed), Width = 24, Height = 32,
                PerceptualHash = AppleStoreArtComposer.PerceptualHash(composed), Frame = "home", Locale = "en-US", Theme = "casaray", Device = "iphone-69",
                SpecSha256 = new string('a', 64), TemplateSha256 = new string('b', 64), CaptionsSha256 = new string('c', 64), RenderSha256 = new string('d', 64),
                Renderer = "test", Inputs = [input]
            };
            var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var provenance = new Dictionary<string, object>
            {
                ["schemaVersion"] = 3, ["repository"] = "EvotecIT/Sample", ["captureRunId"] = "123", ["sourceCommit"] = ApprovedSourceCommit,
                ["marketingVersion"] = "1.5.0", ["workflowRef"] = "EvotecIT/Sample/.github/workflows/apple-screenshots.yml@refs/heads/main",
                ["xcodeVersion"] = "Xcode 26", ["runtime"] = "iOS 26", ["device"] = "iPhone", ["theme"] = "dark", ["scenario"] = "store-home",
                ["screenshots"] = new[] { input }, ["compositions"] = new[] { lineage }
            };
            var config = Path.Combine(root, "screenshots.json");
            File.WriteAllText(config, """{"appId":"1234567890","platform":"iOS","locale":"en-US","screenshotSets":[{"screenshotDisplayType":"APP_IPHONE_67","path":"art","allowedDimensions":["24x32"]}],"quality":{"enabled":true,"minimumFileBytes":1,"minimumKilobytesPerMegapixel":0}}""");
            var provenancePath = Path.Combine(root, "capture.json"); var manifestPath = Path.Combine(root, "approval.json");
            var command = $"\"{GetCliPath(repoRoot)}\" apple-screenshots manifest --config \"{config}\" --capture-provenance \"{provenancePath}\" --expected-repository EvotecIT/Sample --expected-workflow-ref EvotecIT/Sample/.github/workflows/apple-screenshots.yml@refs/heads/main --approved-by reviewer --allowed-root \"{root}\" --out \"{manifestPath}\" --output json";
            File.WriteAllText(provenancePath, JsonSerializer.Serialize(provenance, json));
            var approved = await RunCliAsync(repoRoot, command);
            Assert.True(approved.ExitCode == 0, approved.StdOut + approved.StdErr);
            using (var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath)))
            {
                Assert.Equal(3, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
                var entry = manifest.RootElement.GetProperty("screenshots")[0];
                Assert.Equal(lineage.PerceptualHash, entry.GetProperty("perceptualHash").GetString());
                Assert.Equal(input.Sha256, entry.GetProperty("composition").GetProperty("inputs")[0].GetProperty("sha256").GetString());
            }
            // Local composition also works against an untouched v2 capture inventory.
            provenance["schemaVersion"] = 2; provenance.Remove("compositions");
            var lineagePath = Path.Combine(root, "lineage.json"); File.WriteAllText(lineagePath, JsonSerializer.Serialize(new[] { lineage }, json));
            File.WriteAllText(provenancePath, JsonSerializer.Serialize(provenance, json));
            Assert.Equal(0, (await RunCliAsync(repoRoot, command + $" --store-art-lineage \"{lineagePath}\"")).ExitCode);
            lineage.Locale = "pl-PL"; File.WriteAllText(lineagePath, JsonSerializer.Serialize(new[] { lineage }, json));
            Assert.Equal(1, (await RunCliAsync(repoRoot, command + $" --store-art-lineage \"{lineagePath}\"")).ExitCode);
            lineage.Locale = "en-US"; File.WriteAllText(lineagePath, JsonSerializer.Serialize(new[] { lineage }, json));
            using (var image = new MagickImage(MagickColors.Red, 12, 16)) image.Write(raw, MagickFormat.Png24);
            Assert.Equal(1, (await RunCliAsync(repoRoot, command + $" --store-art-lineage \"{lineagePath}\"")).ExitCode);
            using (var image = new MagickImage(MagickColors.White, 12, 16)) image.Write(raw, MagickFormat.Png24);
            using (var image = new MagickImage(MagickColors.Red, 24, 32)) image.Write(composed, MagickFormat.Png24);
            Assert.Equal(1, (await RunCliAsync(repoRoot, command + $" --store-art-lineage \"{lineagePath}\"")).ExitCode);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
