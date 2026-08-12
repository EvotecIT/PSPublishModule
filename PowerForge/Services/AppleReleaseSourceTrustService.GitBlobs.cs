namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private void EnsureNoCustomGitFilter(string repositoryRoot, string relativePath, string name)
    {
        var attributes = RunGit(repositoryRoot, "check-attr", "-z", "filter", "--", relativePath)
            .StdOut.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        var value = attributes.Length >= 3 ? attributes[2] : "unspecified";
        if (!value.Equals("unspecified", StringComparison.Ordinal) &&
            !value.Equals("unset", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{name} uses custom Git filter '{value}' and cannot be attested to the exact source commit: {relativePath}. " +
                "Exact Apple source inputs may use Git text/EOL normalization but not repository-configuration-dependent clean or smudge filters.");
        }
    }

    private string ComputeRawGitBlobId(string repositoryRoot, string filePath)
    {
        var objectFormat = ReadGitObjectFormat(repositoryRoot);
        using System.Security.Cryptography.HashAlgorithm hash = objectFormat.Equals("sha256", StringComparison.OrdinalIgnoreCase)
            ? System.Security.Cryptography.SHA256.Create()
            : objectFormat.Equals("sha1", StringComparison.OrdinalIgnoreCase)
                ? System.Security.Cryptography.SHA1.Create()
                : throw new InvalidOperationException($"Unsupported Git object format '{objectFormat}'.");
        var length = new FileInfo(filePath).Length;
        var prefix = System.Text.Encoding.ASCII.GetBytes($"blob {length}\0");
        hash.TransformBlock(prefix, 0, prefix.Length, prefix, 0);
        using var stream = File.OpenRead(filePath);
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            hash.TransformBlock(buffer, 0, read, buffer, 0);
        hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return BitConverter.ToString(hash.Hash!).Replace("-", string.Empty).ToLowerInvariant();
    }

    private string ComputeRawGitBlobId(string repositoryRoot, byte[] content)
    {
        var objectFormat = ReadGitObjectFormat(repositoryRoot);
        using System.Security.Cryptography.HashAlgorithm hash = objectFormat.Equals("sha256", StringComparison.OrdinalIgnoreCase)
            ? System.Security.Cryptography.SHA256.Create()
            : objectFormat.Equals("sha1", StringComparison.OrdinalIgnoreCase)
                ? System.Security.Cryptography.SHA1.Create()
                : throw new InvalidOperationException($"Unsupported Git object format '{objectFormat}'.");
        var prefix = System.Text.Encoding.ASCII.GetBytes($"blob {content.LongLength}\0");
        hash.TransformBlock(prefix, 0, prefix.Length, prefix, 0);
        hash.TransformFinalBlock(content, 0, content.Length);
        return BitConverter.ToString(hash.Hash!).Replace("-", string.Empty).ToLowerInvariant();
    }

    private string ComputePathAwareGitBlobId(string repositoryRoot, string filePath, string relativePath)
        => RunGit(repositoryRoot, "hash-object", $"--path={relativePath}", "--", filePath).StdOut.Trim();

    private string ComputePathAwareGitBlobId(string repositoryRoot, byte[] content, string relativePath)
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), ".powerforge-git-filter-" + Guid.NewGuid().ToString("N"));
        var temporaryPath = Path.Combine(temporaryRoot, "captured-input");
        Directory.CreateDirectory(temporaryRoot);
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temporaryRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#endif
        try
        {
            File.WriteAllBytes(temporaryPath, content);
#if NET8_0_OR_GREATER
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
#endif
            return RunGit(repositoryRoot, "hash-object", $"--path={relativePath}", "--", temporaryPath).StdOut.Trim();
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot);
        }
    }
}
