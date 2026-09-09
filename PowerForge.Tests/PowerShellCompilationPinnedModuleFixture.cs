namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    private static void CopyPinnedWorkflowModule(ArtifactFixture fixture, string module, string expectedManifestHash, int expectedFileCount)
    {
        var snapshot = FindCompleteConversionWorkflow(module, "FullModule", module + ".psm1");
        var snapshotRoot = Path.GetDirectoryName(snapshot)!;
        var hashes = Path.Combine(snapshotRoot, "SHA256SUMS.txt");
        Assert.Equal(expectedManifestHash,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(hashes))).ToLowerInvariant());
        var entries = File.ReadAllLines(hashes);
        Assert.Equal(expectedFileCount, entries.Length);
        foreach (var entry in entries)
        {
            var relative = entry[66..];
            var source = Path.Combine(snapshotRoot, relative);
            Assert.Equal(entry[..64], Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant());
            var destination = relative == module + ".psm1" ? fixture.ScriptPath : Path.Combine(fixture.RootPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
    }
}
