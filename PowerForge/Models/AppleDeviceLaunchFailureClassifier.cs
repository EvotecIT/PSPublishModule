namespace PowerForge;

/// <summary>
/// Classifies expected devicectl launch failures that should not invalidate a completed device deployment.
/// </summary>
internal static class AppleDeviceLaunchFailureClassifier
{
    /// <summary>
    /// Detects the CoreDevice/SpringBoard response emitted when an installed app cannot be launched because the device is locked.
    /// </summary>
    /// <param name="result">Launch process result returned by devicectl.</param>
    /// <returns>True when the failure text identifies the device-locked launch rejection.</returns>
    public static bool IsDeviceLocked(ProcessRunResult result)
    {
        if (result.Succeeded)
            return false;

        var text = string.Concat(result.StdErr, "\n", result.StdOut);
        return text.IndexOf("BSErrorCodeDescription = Locked", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("because the device was not, or could not be, unlocked", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
