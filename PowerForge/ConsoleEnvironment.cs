using System;

namespace PowerForge;

/// <summary>
/// Minimal environment detection helpers shared across hosts (CLI/PowerShell/CI).
/// </summary>
public static class ConsoleEnvironment
{
    /// <summary>
    /// Returns <c>true</c> when running in a CI environment (GitHub Actions, Azure DevOps, AppVeyor, etc.).
    /// </summary>
    public static bool IsCI =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")) ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD")) ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPVEYOR"));
}

