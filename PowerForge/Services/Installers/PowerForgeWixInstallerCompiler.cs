using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PowerForge;

/// <summary>
/// Prepares and optionally compiles generated WiX installer source with the WiX SDK.
/// </summary>
public sealed class PowerForgeWixInstallerCompiler
{
    private readonly PowerForgeWixInstallerSourceEmitter _emitter;
    private readonly IProcessRunner _processRunner;

    /// <summary>
    /// Initializes a new instance of the <see cref="PowerForgeWixInstallerCompiler"/> class.
    /// </summary>
    /// <param name="emitter">WiX source emitter.</param>
    /// <param name="processRunner">Process runner used for dotnet build.</param>
    public PowerForgeWixInstallerCompiler(
        PowerForgeWixInstallerSourceEmitter? emitter = null,
        IProcessRunner? processRunner = null)
    {
        _emitter = emitter ?? new PowerForgeWixInstallerSourceEmitter();
        _processRunner = processRunner ?? new ProcessRunner();
    }

    /// <summary>
    /// Writes generated WiX source and a WiX SDK project into the requested workspace.
    /// </summary>
    /// <param name="definition">Installer definition.</param>
    /// <param name="request">Compile request.</param>
    /// <returns>Prepared workspace file paths.</returns>
    public PowerForgeWixInstallerWorkspace PrepareWorkspace(
        PowerForgeInstallerDefinition definition,
        PowerForgeWixInstallerCompileRequest request)
    {
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.WorkingDirectory))
            throw new InvalidOperationException("WiX compile request requires WorkingDirectory.");
        if (string.IsNullOrWhiteSpace(request.SourceFileName))
            throw new InvalidOperationException("WiX compile request requires SourceFileName.");
        if (string.IsNullOrWhiteSpace(request.ProjectFileName))
            throw new InvalidOperationException("WiX compile request requires ProjectFileName.");

        var workingDirectory = Path.GetFullPath(request.WorkingDirectory);
        Directory.CreateDirectory(workingDirectory);

        var sourcePath = Path.Combine(workingDirectory, request.SourceFileName);
        var projectPath = Path.Combine(workingDirectory, request.ProjectFileName);

        var projectOptions = request.CreateProjectOptions();
        projectOptions.SourceFile = request.SourceFileName;

        File.WriteAllText(
            sourcePath,
            _emitter.EmitSource(definition),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.WriteAllText(
            projectPath,
            _emitter.EmitProjectFile(definition, projectOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new PowerForgeWixInstallerWorkspace
        {
            WorkingDirectory = workingDirectory,
            SourcePath = sourcePath,
            ProjectPath = projectPath
        };
    }

    /// <summary>
    /// Writes generated WiX files and runs dotnet build against the generated WiX SDK project.
    /// </summary>
    /// <param name="definition">Installer definition.</param>
    /// <param name="request">Compile request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Compile result.</returns>
    public async Task<PowerForgeWixInstallerCompileResult> CompileAsync(
        PowerForgeInstallerDefinition definition,
        PowerForgeWixInstallerCompileRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var workspace = PrepareWorkspace(definition, request);

        var args = new List<string>
        {
            "build",
            workspace.ProjectPath,
            "-c",
            request.Configuration,
            "--nologo"
        };
        if (request.NoRestore)
            args.Add("--no-restore");

        var processRequest = new ProcessRunRequest(
            request.DotNetExecutable,
            workspace.WorkingDirectory,
            args,
            request.Timeout);
        var processResult = await _processRunner.RunAsync(processRequest, cancellationToken).ConfigureAwait(false);

        return new PowerForgeWixInstallerCompileResult
        {
            WorkingDirectory = workspace.WorkingDirectory,
            SourcePath = workspace.SourcePath,
            ProjectPath = workspace.ProjectPath,
            ExitCode = processResult.ExitCode,
            StdOut = processResult.StdOut,
            StdErr = processResult.StdErr,
            TimedOut = processResult.TimedOut
        };
    }
}

/// <summary>
/// Request for preparing or compiling generated WiX installer source.
/// </summary>
public sealed class PowerForgeWixInstallerCompileRequest
{
    /// <summary>
    /// Working directory where generated WiX files are written.
    /// </summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Generated WiX source filename.
    /// </summary>
    public string SourceFileName { get; set; } = "Product.wxs";

    /// <summary>
    /// Generated WiX SDK project filename.
    /// </summary>
    public string ProjectFileName { get; set; } = "Installer.wixproj";

    /// <summary>
    /// Dotnet executable used to run the WiX SDK build.
    /// </summary>
    public string DotNetExecutable { get; set; } = "dotnet";

    /// <summary>
    /// Build configuration passed to dotnet build.
    /// </summary>
    public string Configuration { get; set; } = "Release";

    /// <summary>
    /// When true, passes --no-restore to dotnet build.
    /// </summary>
    public bool NoRestore { get; set; }

    /// <summary>
    /// Process timeout for dotnet build.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// WiX SDK version used by the generated project.
    /// </summary>
    public string SdkVersion { get; set; } = "4.0.6";

    /// <summary>
    /// WiX platform used by the generated project.
    /// </summary>
    public string Platform { get; set; } = "x64";

    /// <summary>
    /// MSBuild define constants passed to WiX.
    /// </summary>
    public Dictionary<string, string> DefineConstants { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Additional WiX source files included by the generated project.
    /// </summary>
    public List<string> AdditionalSourceFiles { get; } = new();

    internal PowerForgeWixInstallerProjectOptions CreateProjectOptions()
    {
        var options = new PowerForgeWixInstallerProjectOptions
        {
            SdkVersion = SdkVersion,
            Platform = Platform,
            SourceFile = SourceFileName
        };
        foreach (var entry in DefineConstants)
            options.DefineConstants[entry.Key] = entry.Value;
        foreach (var sourceFile in AdditionalSourceFiles)
            options.AdditionalSourceFiles.Add(sourceFile);
        return options;
    }
}

/// <summary>
/// File paths prepared for a generated WiX workspace.
/// </summary>
public sealed class PowerForgeWixInstallerWorkspace
{
    /// <summary>
    /// Working directory containing generated files.
    /// </summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Generated WiX source path.
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Generated WiX SDK project path.
    /// </summary>
    public string ProjectPath { get; set; } = string.Empty;
}

/// <summary>
/// Result from compiling a generated WiX installer project.
/// </summary>
public sealed class PowerForgeWixInstallerCompileResult
{
    /// <summary>
    /// Working directory containing generated files.
    /// </summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Generated WiX source path.
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Generated WiX SDK project path.
    /// </summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>
    /// Dotnet build exit code.
    /// </summary>
    public int ExitCode { get; set; }

    /// <summary>
    /// Captured standard output.
    /// </summary>
    public string StdOut { get; set; } = string.Empty;

    /// <summary>
    /// Captured standard error.
    /// </summary>
    public string StdErr { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether the dotnet build process timed out.
    /// </summary>
    public bool TimedOut { get; set; }

    /// <summary>
    /// Indicates whether dotnet build succeeded.
    /// </summary>
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}
