using System.Diagnostics;
using System.Text;

namespace PowerForge.Web;

/// <summary>Options for dotnet build.</summary>
public sealed class WebDotNetBuildOptions
{
    /// <summary>Project or solution path.</summary>
    public string ProjectOrSolution { get; set; } = string.Empty;
    /// <summary>Build configuration.</summary>
    public string? Configuration { get; set; }
    /// <summary>Target framework.</summary>
    public string? Framework { get; set; }
    /// <summary>Runtime identifier.</summary>
    public string? Runtime { get; set; }
    /// <summary>When true, restore packages.</summary>
    public bool Restore { get; set; } = true;
}

/// <summary>Options for dotnet publish.</summary>
public sealed class WebDotNetPublishOptions
{
    /// <summary>Project path.</summary>
    public string ProjectPath { get; set; } = string.Empty;
    /// <summary>Output directory.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Build configuration.</summary>
    public string? Configuration { get; set; }
    /// <summary>Target framework.</summary>
    public string? Framework { get; set; }
    /// <summary>Runtime identifier.</summary>
    public string? Runtime { get; set; }
    /// <summary>Whether to publish as self-contained.</summary>
    public bool SelfContained { get; set; } = false;
    /// <summary>Skip build step.</summary>
    public bool NoBuild { get; set; } = false;
    /// <summary>Skip restore step.</summary>
    public bool NoRestore { get; set; } = false;
    /// <summary>Optional MSBuild DefineConstants override.</summary>
    public string? DefineConstants { get; set; }
}

/// <summary>Result payload for dotnet build/publish commands.</summary>
public sealed class WebDotNetResult
{
    /// <summary>Execution success status.</summary>
    public bool Success { get; set; }
    /// <summary>Process exit code.</summary>
    public int ExitCode { get; set; }
    /// <summary>Captured stdout.</summary>
    public string Output { get; set; } = string.Empty;
    /// <summary>Captured stderr.</summary>
    public string Error { get; set; } = string.Empty;
}

/// <summary>Runs dotnet build and publish commands.</summary>
public static class WebDotNetRunner
{
    /// <summary>Executes dotnet build with provided options.</summary>
    /// <param name="options">Build options.</param>
    /// <returns>Execution result.</returns>
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

    /// <summary>Executes dotnet publish with provided options.</summary>
    /// <param name="options">Publish options.</param>
    /// <returns>Execution result.</returns>
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
        if (!string.IsNullOrWhiteSpace(options.DefineConstants))
        {
            var defineConstants = options.DefineConstants.Replace(";", "%3B", StringComparison.Ordinal);
            args.Add($"-p:DefineConstants={defineConstants}");
        }

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
