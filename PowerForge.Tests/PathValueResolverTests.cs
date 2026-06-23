using PowerForge;

namespace PowerForge.Tests;

public sealed class PathValueResolverTests
{
    [Fact]
    public void NormalizeSeparators_accepts_either_slash_style()
    {
        var normalized = PathValueResolver.NormalizeSeparators("Build\\..\\Artefacts/UploadReady");

        Assert.Equal(Normalize("Build/../Artefacts/UploadReady"), normalized);
    }

    [Fact]
    public void Resolve_treats_powershell_authored_absolute_path_as_native_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-path-" + Guid.NewGuid().ToString("N"));
        var authoredPath = root + "\\Build\\..\\Artefacts\\UploadReady";

        var resolved = PathValueResolver.Resolve(Path.GetTempPath(), authoredPath);

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "Artefacts", "UploadReady")), resolved);
    }

    [Fact]
    public void Resolve_combines_relative_powershell_path_with_base_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-path-base-" + Guid.NewGuid().ToString("N"));

        var resolved = PathValueResolver.Resolve(root, "Build\\project.build.json");

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "Build", "project.build.json")), resolved);
    }

    private static string Normalize(string value)
    {
        return OperatingSystem.IsWindows()
            ? value.Replace('/', '\\')
            : value.Replace('\\', '/');
    }
}
