using System.Diagnostics;
using System.Text;

namespace PowerForge.Web;

public sealed class WebDotNetBuildOptions
{
    public string ProjectOrSolution { get; set; } = string.Empty;
    public string? Configuration { get; set; }
    public string? Framework { get; set; }
    public string? Runtime { get; set; }
    public bool Restore { get; set; } = true;
}

public sealed class WebDotNetPublishOptions
{
    public string ProjectPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string? Configuration { get; set; }
    public string? Framework { get; set; }
    public string? Runtime { get; set; }
    public bool SelfContained { get; set; } = false;
    public bool NoBuild { get; set; } = false;
    public bool NoRestore { get; set; } = false;
}

public sealed class WebDotNetResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

public static class WebDotNetRunner
{
    public static WebDotNetResult Build(WebDotNetBuildOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.ProjectOrSolution))
            throw new ArgumentException("ProjectOrSolution is required.", nameof(options));

        var args = new List<string> { "build", options.ProjectOrSolution };
        if (!string.IsNullOrWhiteSpace(options.Configuration))
            args.AddRange(new[] { "-c", options.Configuration });
        if (!string.IsNullOrWhiteSpace(options.Framework))
            args.AddRange(new[] { "-f", options.Framework });
        if (!string.IsNullOrWhiteSpace(options.Runtime))
            args.AddRange(new[] { "-r", options.Runtime });
        if (!options.Restore)
            args.Add("--no-restore");

        return Run("dotnet", args);
    }

    public static WebDotNetResult Publish(WebDotNetPublishOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.ProjectPath))
            throw new ArgumentException("ProjectPath is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new ArgumentException("OutputPath is required.", nameof(options));

        var args = new List<string> { "publish", options.ProjectPath, "-o", options.OutputPath };
        if (!string.IsNullOrWhiteSpace(options.Configuration))
            args.AddRange(new[] { "-c", options.Configuration });
        if (!string.IsNullOrWhiteSpace(options.Framework))
            args.AddRange(new[] { "-f", options.Framework });
        if (!string.IsNullOrWhiteSpace(options.Runtime))
            args.AddRange(new[] { "-r", options.Runtime });
        if (options.SelfContained)
            args.Add("--self-contained");
        if (options.NoBuild)
            args.Add("--no-build");
        if (options.NoRestore)
            args.Add("--no-restore");

        return Run("dotnet", args);
    }

    private static WebDotNetResult Run(string fileName, IEnumerable<string> args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            output.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            error.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return new WebDotNetResult
        {
            ExitCode = process.ExitCode,
            Success = process.ExitCode == 0,
            Output = output.ToString().TrimEnd(),
            Error = error.ToString().TrimEnd()
        };
    }
}
