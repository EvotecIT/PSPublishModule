namespace PowerForge;

public sealed partial class DotNetPublishReleaseArtifactVerifier
{
    internal static bool ChecksumContains(string path, string relativePath, string digest)
    {
        string expected = relativePath.Replace('\\', '/');
        StringComparison pathComparison = FrameworkCompatibility.GetPathStringComparisonForPath(path);
        bool found = false;
        foreach (string line in File.ReadLines(path))
        {
            if (!TryParseChecksumLine(line, out string listedDigest, out string listedPath) ||
                !string.Equals(listedPath.Replace('\\', '/'), expected, pathComparison))
                continue;
            if (found || !string.Equals(listedDigest, digest, StringComparison.OrdinalIgnoreCase))
                return false;
            found = true;
        }

        return found;
    }

    private static bool TryParseChecksumLine(string line, out string digest, out string relativePath)
    {
        digest = string.Empty;
        relativePath = string.Empty;
        const int digestLength = 64;
        const int markerOffset = digestLength + 1;
        const int pathOffset = markerOffset + 1;
        if (line.Length <= pathOffset || line[digestLength] != ' ' ||
            (line[markerOffset] != ' ' && line[markerOffset] != '*'))
            return false;

        for (int index = 0; index < digestLength; index++)
        {
            char value = line[index];
            if (!((value >= '0' && value <= '9') ||
                  (value >= 'a' && value <= 'f') ||
                  (value >= 'A' && value <= 'F')))
                return false;
        }

        string path = line.Substring(pathOffset);
        if (path.Length == 0 || path.IndexOf('\0') >= 0)
            return false;
        digest = line.Substring(0, digestLength);
        relativePath = path;
        return true;
    }
}
