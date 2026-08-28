namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool TryMapControlledPublishInputPath(
        string controlledPath,
        string controlledSourceRoot,
        string originalGitRoot,
        string controlledPackageRoot,
        IReadOnlyCollection<VerifiedPackageInputCatalog> verifiedPackages,
        out string originalPath,
        out bool isPackageBacked)
    {
        originalPath = string.Empty;
        isPackageBacked = false;
        if (IsSameOrBelowBuildInputPath(controlledPath, controlledSourceRoot))
        {
            string relativePath = FrameworkCompatibility.GetRelativePath(
                controlledSourceRoot,
                controlledPath);
            originalPath = Path.GetFullPath(Path.Combine(originalGitRoot, relativePath));
            return IsSameOrBelowBuildInputPath(originalPath, originalGitRoot);
        }

        if (string.IsNullOrWhiteSpace(controlledPackageRoot) ||
            !IsSameOrBelowBuildInputPath(controlledPath, controlledPackageRoot))
        {
            return false;
        }

        foreach (VerifiedPackageInputCatalog packageCatalog in verifiedPackages)
        {
            if (!packageCatalog.TryMapControlledPackageInput(
                    controlledPath,
                    controlledPackageRoot,
                    out originalPath))
            {
                continue;
            }
            isPackageBacked = true;
            return true;
        }
        originalPath = string.Empty;
        return false;
    }

    private static bool TryMapControlledPublishMetadata(
        IReadOnlyDictionary<string, string> metadata,
        string controlledSourceRoot,
        string originalGitRoot,
        string controlledPackageRoot,
        string controlledOutputRoot,
        IReadOnlyCollection<VerifiedPackageInputCatalog> verifiedPackages,
        out IReadOnlyDictionary<string, string> mappedMetadata)
    {
        var mapped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> entry in metadata)
        {
            string value = RemapControlledPublishSourceValue(
                entry.Value ?? string.Empty,
                controlledSourceRoot,
                originalGitRoot);
            if (string.Equals(value, entry.Value, StringComparison.Ordinal) &&
                Path.IsPathRooted(value) &&
                !string.IsNullOrWhiteSpace(controlledPackageRoot) &&
                IsSameOrBelowBuildInputPath(value, controlledPackageRoot))
            {
                bool packageMapped = false;
                foreach (VerifiedPackageInputCatalog packageCatalog in verifiedPackages)
                {
                    if (!packageCatalog.TryMapControlledPackageInput(
                            value,
                            controlledPackageRoot,
                            out string packagePath))
                    {
                        continue;
                    }
                    value = packagePath;
                    packageMapped = true;
                    break;
                }
                if (!packageMapped)
                {
                    mappedMetadata = new Dictionary<string, string>();
                    return false;
                }
            }

            if (ContainsControlledPublishPath(value, controlledSourceRoot) ||
                ContainsControlledPublishPath(value, controlledPackageRoot) ||
                ContainsControlledPublishPath(value, controlledOutputRoot))
            {
                mappedMetadata = new Dictionary<string, string>();
                return false;
            }
            mapped[entry.Key] = value;
        }
        mappedMetadata = mapped;
        return true;
    }

    internal static string RemapControlledPublishSourceValue(
        string value,
        string controlledSourceRoot,
        string originalGitRoot)
        => ReplaceControlledPathRoot(value, controlledSourceRoot, originalGitRoot);

    private static bool ContainsControlledPublishPath(string value, string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return false;
        return value.IndexOf(
            Path.GetFullPath(root),
            IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) >= 0;
    }
}
