using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class DotNetPublishThirdPartyNoticesTests
{
    [Fact]
    public void Generate_RequiresCoverageOfExactRuntimePackagesAndIncludesNativeNotices()
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForgeNotices", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string license = Path.Combine(root, "LICENSE.txt");
            File.WriteAllText(license, "Copyright Example. Permission to redistribute. Native dependency notices.");
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(license))).ToLowerInvariant();
            File.WriteAllText(Path.Combine(root, "deps.json"), """
                {"runtimeTarget":{"name":"net10.0/osx-arm64"},"libraries":{"Native/1.2.3":{"type":"package"},"App/1.0":{"type":"project"}}}
                """);
            File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(new {
                SchemaVersion = 1, Packages = new[] { new {
                    Package = "Native/1.2.3", Authors = "Example", License = "MIT", Files = new[] {
                        new { Path = "LICENSE.txt", Sha256 = hash, Source = "upstream" }
                    }
                } }
            }));
            string output = Path.Combine(root, "output");
            DotNetPublishThirdPartyNotices.Generate(root, Path.Combine(root, "deps.json"), Path.Combine(root, "manifest.json"), output);
            Assert.Contains("Native dependency notices", File.ReadAllText(Path.Combine(output, "THIRD_PARTY_NOTICES.txt")));
            Assert.Contains("Native/1.2.3", File.ReadAllText(Path.Combine(output, "runtime-package-inventory.json")));
            File.WriteAllText(license, "Changed license text");
            Assert.Throws<InvalidOperationException>(() => DotNetPublishThirdPartyNotices.Generate(root,
                Path.Combine(root, "deps.json"), Path.Combine(root, "manifest.json"), output));
            File.WriteAllText(Path.Combine(root, "deps.json"), """
                {"runtimeTarget":{"name":"net10.0/osx-arm64"},"libraries":{"Native/1.2.4":{"type":"package"}}}
                """);
            var missing = Assert.Throws<InvalidOperationException>(() => DotNetPublishThirdPartyNotices.Generate(root,
                Path.Combine(root, "deps.json"), Path.Combine(root, "manifest.json"), output));
            Assert.Contains("Native/1.2.4", missing.Message);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
