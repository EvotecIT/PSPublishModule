namespace PowerForge;

internal static class DotNetPublishReleaseAssetNaming
{
    internal static string CreateDirectMatrixAssetName(
        string target,
        string framework,
        string runtime,
        string style,
        DotNetPublishArtefactCategory category,
        string? bundleId,
        string sourcePath)
    {
        string categoryName = category == DotNetPublishArtefactCategory.Bundle
            ? "bundle-" + ToSafeComponent(bundleId ?? "unnamed")
            : "publish";
        return string.Join(
            "-",
            ToSafeComponent(target),
            ToSafeComponent(framework),
            ToSafeComponent(runtime),
            ToSafeComponent(style),
            categoryName) + Path.GetExtension(sourcePath);
    }

    internal static string ToSafeComponent(string value)
    {
        string safe = string.Concat((value ?? string.Empty).Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) || character is '/' or '\\' or ':'
                ? '_'
                : character));
        return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe;
    }
}
