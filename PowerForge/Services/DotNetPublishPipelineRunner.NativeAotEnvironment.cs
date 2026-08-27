namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal static string BuildTrustedNativeAotPath(
        string dotNetExecutablePath,
        string? inheritedPath)
    {
        string dotNetDirectory = Path.GetDirectoryName(Path.GetFullPath(dotNetExecutablePath))
            ?? throw new InvalidOperationException("The trusted dotnet executable has no containing directory.");
        StringComparer comparer = IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var trustedRoots = new HashSet<string>(comparer);
        var requiredDirectories = new List<string>();
        if (IsWindows())
        {
            AddTrustedNativeToolchainRoot(
                trustedRoots,
                Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            AddTrustedNativeToolchainRoot(
                trustedRoots,
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            AddTrustedNativeToolchainRoot(
                trustedRoots,
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (!string.IsNullOrWhiteSpace(systemDirectory))
                requiredDirectories.Add(systemDirectory);
        }
        else
        {
            AddTrustedNativeToolchainRoot(trustedRoots, "/usr/bin");
            AddTrustedNativeToolchainRoot(trustedRoots, "/usr/sbin");
            AddTrustedNativeToolchainRoot(trustedRoots, "/bin");
            AddTrustedNativeToolchainRoot(trustedRoots, "/sbin");
            AddTrustedNativeToolchainRoot(trustedRoots, "/Applications/Xcode.app");
            AddTrustedNativeToolchainRoot(trustedRoots, "/Library/Developer/CommandLineTools");
            requiredDirectories.Add("/usr/bin");
            requiredDirectories.Add("/usr/sbin");
        }

        var admitted = new List<string> { dotNetDirectory };
        IEnumerable<string> candidates = (inheritedPath ?? string.Empty)
            .Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries)
            .Concat(requiredDirectories);
        foreach (string candidate in candidates)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(candidate.Trim().Trim('"'));
            }
            catch
            {
                continue;
            }
            if (!Directory.Exists(fullPath) || admitted.Contains(fullPath, comparer))
                continue;
            string? allowedRoot = trustedRoots.FirstOrDefault(root =>
                IsSameOrBelowBuildInputPath(fullPath, root));
            if (allowedRoot is null ||
                IsReparsePointPath(allowedRoot) ||
                HasReparsePointInExistingAncestors(fullPath, allowedRoot))
            {
                continue;
            }
            admitted.Add(fullPath);
        }
        return string.Join(Path.PathSeparator.ToString(), admitted);
    }

    private static void AddTrustedNativeToolchainRoot(ISet<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            string fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
                roots.Add(fullPath);
        }
        catch
        {
            // Ignore unavailable platform roots; NativeAOT will fail closed if its toolchain is absent.
        }
    }
}
