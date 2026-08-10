namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private static void ValidateTrustedAppleToolExecutables(PowerForgeAppleReleaseOptions options)
    {
        ValidateTrustedAppleToolExecutable(
            options.XcodeBuildExecutable,
            "xcodebuild",
            "/usr/bin/xcodebuild");
        ValidateTrustedAppleToolExecutable(
            options.DirectDistribution.XcrunExecutable,
            "xcrun",
            "/usr/bin/xcrun");
        ValidateTrustedAppleToolExecutable(
            options.DirectDistribution.DittoExecutable,
            "ditto",
            "/usr/bin/ditto");
        ValidateTrustedAppleToolExecutable(
            options.DirectDistribution.SpctlExecutable,
            "spctl",
            "/usr/sbin/spctl");
    }

    private static void ValidateTrustedAppleToolExecutable(
        string? executable,
        string defaultName,
        string trustedPath)
    {
        var value = executable?.Trim();
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, defaultName, StringComparison.Ordinal) ||
            string.Equals(value, trustedPath, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Exact-source Apple checkpoints require the trusted system tool '{trustedPath}'; configured executable '{value}' is not trusted.");
    }
}
