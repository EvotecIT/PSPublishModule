namespace PowerForge.Tests;

internal static class AppleDeploymentTestFixture
{
    internal static void WriteSharedSchemes(
        string workingDirectory,
        params string[] schemeNames)
    {
        foreach (var project in Directory.EnumerateDirectories(
                     workingDirectory,
                     "*.xcodeproj",
                     SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(project) & FileAttributes.ReparsePoint) != 0)
                continue;

            var schemeRoot = Directory.CreateDirectory(Path.Combine(
                project,
                "xcshareddata",
                "xcschemes"));
            foreach (var scheme in schemeNames
                         .Append(Path.GetFileNameWithoutExtension(project))
                         .Distinct(StringComparer.Ordinal))
            {
                File.WriteAllText(
                    Path.Combine(schemeRoot.FullName, scheme + ".xcscheme"),
                    "<Scheme />");
            }
        }
    }
}
