namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private static void ValidateTrustedXcodeBuildExecutable(string? executable)
    {
        var value = executable?.Trim();
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "xcodebuild", StringComparison.Ordinal) ||
            string.Equals(value, "/usr/bin/xcodebuild", StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Exact-source Apple checkpoints require the system Xcode build tool '/usr/bin/xcodebuild'; configured executable '{value}' is not trusted.");
    }
}
