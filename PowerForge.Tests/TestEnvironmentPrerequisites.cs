using System.Diagnostics;
using Xunit;

namespace PowerForge.Tests;

/// <summary>Facts about the machine running the tests, probed once instead of inferred from the operating system.</summary>
internal static class TestEnvironment
{
    private static readonly Lazy<bool> TempIsCaseSensitive = new(ProbeTempCaseSensitivity);
    private static readonly Lazy<int?> PwshRuntimeMajor = new(ProbePwshRuntimeMajor);

    /// <summary>True when the temporary folder distinguishes names that differ only in case (macOS volumes usually do not).</summary>
    internal static bool IsTempFolderCaseSensitive => TempIsCaseSensitive.Value;

    /// <summary>The major .NET version of the pwsh on PATH, or null when pwsh is unavailable.</summary>
    internal static int? PwshDotNetMajor => PwshRuntimeMajor.Value;

    private static bool ProbeTempCaseSensitivity()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-case-probe-" + Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "probe"), string.Empty);
            return !File.Exists(Path.Combine(directory.FullName, "PROBE"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static int? ProbePwshRuntimeMajor()
    {
        try
        {
            var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "[Environment]::Version.Major" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(30_000)) { process.Kill(entireProcessTree: true); return null; }
            return process.ExitCode == 0 && int.TryParse(output.Trim(), out var major) ? major : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Runs only where the temporary folder is case-sensitive.</summary>
internal sealed class CaseSensitiveTempFactAttribute : FactAttribute
{
    public CaseSensitiveTempFactAttribute()
    {
        if (!TestEnvironment.IsTempFolderCaseSensitive)
            Skip = "The temporary folder's file system is case-insensitive, so case-distinct names cannot coexist.";
    }
}

/// <summary>Runs only when pwsh on PATH can load the net10.0 test fixtures it imports as module payloads.</summary>
internal sealed class PwshNet10FactAttribute : FactAttribute
{
    public PwshNet10FactAttribute()
    {
        if (TestEnvironment.PwshDotNetMajor is not { } major || major < 10)
            Skip = PwshNet10Requirement.Reason;
    }
}

/// <summary>Theory form of <see cref="PwshNet10FactAttribute"/>.</summary>
internal sealed class PwshNet10TheoryAttribute : TheoryAttribute
{
    public PwshNet10TheoryAttribute()
    {
        if (TestEnvironment.PwshDotNetMajor is not { } major || major < 10)
            Skip = PwshNet10Requirement.Reason;
    }
}

internal static class PwshNet10Requirement
{
    internal static string Reason =>
        TestEnvironment.PwshDotNetMajor is { } major
            ? $"pwsh on PATH runs .NET {major}; these tests import net10.0 fixture assemblies, which need PowerShell 7.6 or newer."
            : "pwsh is not on PATH; these tests import net10.0 fixture assemblies into PowerShell 7.6 or newer.";
}
