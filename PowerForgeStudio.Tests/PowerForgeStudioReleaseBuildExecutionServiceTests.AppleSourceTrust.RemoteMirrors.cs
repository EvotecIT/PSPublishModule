using PowerForge;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Fact]
    public void InitializeRemotePackageMirror_uses_revision_object_format()
    {
        using var scope = new TemporaryDirectoryScope();
        var mirror = scope.CreateDirectory("Sha256RemoteMirror");

        new AppleReleaseSourceTrustService().InitializeRemotePackageMirror(mirror, new string('a', 64));

        var config = File.ReadAllText(Path.Combine(mirror, "config"));
        Assert.Contains("objectformat = sha256", config, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureRemotePackageMirror_rejects_existing_mismatched_object_format_before_fetch()
    {
        using var scope = new TemporaryDirectoryScope();
        var mirror = scope.CreateDirectory("Sha1RemoteMirror");
        RunGit(mirror, "init", "--quiet", "--bare", "--object-format=sha1");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new AppleReleaseSourceTrustService().EnsureRemotePackageMirror(
                mirror,
                "https://example.invalid/Package.git",
                new string('b', 64)));

        Assert.Contains("object format", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sha256", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
