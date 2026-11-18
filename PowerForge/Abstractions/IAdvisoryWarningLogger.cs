namespace PowerForge;

/// <summary>
/// Logger that can show a warning without turning it into a failure, for optional steps
/// that run after a release has already published (for example under <c>-WarningAction Stop</c>).
/// </summary>
internal interface IAdvisoryWarningLogger
{
    /// <summary>Shows <paramref name="message"/> as a warning that never stops the caller.</summary>
    void WarnAdvisory(string message);
}
