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
        => _ = AppleTrustedExecutionEnvironment.ResolveSystemTool(
            executable,
            defaultName,
            trustedPath,
            "Exact-source Apple checkpoints");
}
