namespace PowerForge;

/// <summary>
/// Public helpers for inspecting text files in a project (encoding, line endings, paths).
/// </summary>
public static class ProjectTextInspection
{
    /// <summary>
    /// Detects the logical encoding kind using BOM and best-effort heuristics.
    /// </summary>
    public static TextEncodingKind DetectEncodingKind(string path)
        => ProjectTextDetector.DetectEncodingKind(path);

    /// <summary>
    /// Detects line ending kind and whether the file ends with a final newline.
    /// </summary>
    public static (DetectedLineEndingKind Kind, bool HasFinalNewline) DetectLineEnding(string path)
        => ProjectTextDetector.DetectLineEnding(path);

    /// <summary>
    /// Computes a relative path in a net472-compatible way.
    /// </summary>
    public static string ComputeRelativePath(string baseDirectory, string fullPath)
        => ProjectTextDetector.ComputeRelativePath(baseDirectory, fullPath);
}

