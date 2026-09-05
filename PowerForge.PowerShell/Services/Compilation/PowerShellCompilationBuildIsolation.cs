using System.Text;

namespace PowerForge;

/// <summary>Writes the compiler-owned MSBuild and NuGet boundary used by generated projects.</summary>
internal static class PowerShellCompilationBuildIsolation
{
    internal static void Write(string directory, bool requireSdkSelection, bool offlineRestore = false)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Build-isolation directory is required.", nameof(directory));
        Directory.CreateDirectory(directory);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        File.WriteAllText(Path.Combine(directory, "Directory.Build.props"), "<Project />" + Environment.NewLine, utf8);
        File.WriteAllText(Path.Combine(directory, "Directory.Build.targets"), "<Project />" + Environment.NewLine, utf8);
        File.WriteAllText(
            Path.Combine(directory, "Directory.Packages.props"),
            "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>" + Environment.NewLine,
            utf8);
        var packageSources = offlineRestore
            ? "<clear />"
            : "<clear /><add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" protocolVersion=\"3\" />";
        File.WriteAllText(
            Path.Combine(directory, "NuGet.Config"),
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine +
            $"<configuration><packageSources>{packageSources}</packageSources></configuration>" + Environment.NewLine,
            utf8);

        if (requireSdkSelection && !File.Exists(Path.Combine(directory, "global.json")))
            throw new InvalidOperationException("Generated source publication did not receive the exact SDK selection used by the artifact build.");
    }
}
