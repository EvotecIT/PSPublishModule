using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebApiDocsGeneratorAssemblyLoadTests
{
    [Fact]
    public void GetApiDocsHostProbeDirectories_IncludesAssemblyDirectoryAndAppBase()
    {
        var assemblyPath = typeof(WebApiDocsGenerator).Assembly.Location;

        var directories = WebApiDocsGenerator.GetApiDocsHostProbeDirectories(assemblyPath);

        Assert.Contains(Path.GetDirectoryName(assemblyPath)!, directories, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFullPath(AppContext.BaseDirectory), directories, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetApiDocsHostProbeDirectories_IncludesPsHomeWhenConfigured()
    {
        var assemblyPath = typeof(WebApiDocsGenerator).Assembly.Location;
        var temporaryPsHome = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-web-apidocs-pshome-" + Guid.NewGuid().ToString("N"))).FullName;
        var originalPsHome = Environment.GetEnvironmentVariable("PSHOME");

        try
        {
            Environment.SetEnvironmentVariable("PSHOME", temporaryPsHome);

            var directories = WebApiDocsGenerator.GetApiDocsHostProbeDirectories(assemblyPath);

            Assert.Contains(Path.GetFullPath(temporaryPsHome), directories, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSHOME", originalPsHome);
            TryDeleteDirectory(temporaryPsHome);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // ignore cleanup failures in tests
        }
    }
}
