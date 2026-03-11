using PowerForge;

namespace PowerForge.Tests;

public sealed class AuthenticodeSigningServiceTests
{
    [Fact]
    public void EnumerateFiles_FiltersByPattern_AndSkipsInternalsAndExcludedPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-signing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var publicDir = Directory.CreateDirectory(Path.Combine(root, "Public"));
            var internalsDir = Directory.CreateDirectory(Path.Combine(root, "Internals"));
            var excludedDir = Directory.CreateDirectory(Path.Combine(root, "IgnoreMe"));

            var publicScript = Path.Combine(publicDir.FullName, "Invoke-Test.ps1");
            var publicManifest = Path.Combine(publicDir.FullName, "Module.psd1");
            var internalScript = Path.Combine(internalsDir.FullName, "Hidden.ps1");
            var excludedScript = Path.Combine(excludedDir.FullName, "Skip.ps1");
            var textFile = Path.Combine(publicDir.FullName, "notes.txt");

            File.WriteAllText(publicScript, "Write-Host test");
            File.WriteAllText(publicManifest, "@{}");
            File.WriteAllText(internalScript, "Write-Host hidden");
            File.WriteAllText(excludedScript, "Write-Host excluded");
            File.WriteAllText(textFile, "nope");

            var service = new AuthenticodeSigningService(new NullLogger());
            var files = service.EnumerateFiles(root, new[] { "*.ps1", "*.psd1" }, new[] { "IgnoreMe" });

            Assert.Contains(publicScript, files, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(publicManifest, files, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(internalScript, files, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(excludedScript, files, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(textFile, files, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
