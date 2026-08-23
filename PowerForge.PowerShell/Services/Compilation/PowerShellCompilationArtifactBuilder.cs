using System.Management.Automation.Language;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// Produces runtime-packaged executables and genuinely typed CLR libraries from PowerShell source.
/// </summary>
public sealed class PowerShellCompilationArtifactBuilder
{
    private const string TypedProjectTemplate = "PowerForge.PowerShell.Compilation.TypedLibrary.csproj.template";
    private const string PackagedProjectTemplate = "PowerForge.PowerShell.Compilation.PackagedExecutable.csproj.template";
    private const string PackagedProgramTemplate = "PowerForge.PowerShell.Compilation.PackagedProgram.cs.template";
    private const string TypedExecutableProjectTemplate = "PowerForge.PowerShell.Compilation.TypedExecutable.csproj.template";
    private const string BinaryModuleProjectTemplate = "PowerForge.PowerShell.Compilation.BinaryModule.csproj.template";
    private const int MaximumBuildOutputLength = 64 * 1024;
    private static readonly HashSet<string> CommonCmdletParameterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Verbose", "Debug", "ErrorAction", "WarningAction", "InformationAction", "ProgressAction",
        "ErrorVariable", "WarningVariable", "InformationVariable", "OutVariable", "OutBuffer", "PipelineVariable",
        "WhatIf", "Confirm", "UseTransaction"
    };

    /// <summary>Builds the requested PowerShell artifact.</summary>
    public PowerShellCompilationBuildResult Build(PowerShellCompilationBuildSpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        ValidateSpec(spec);

        Directory.CreateDirectory(spec.OutputDirectory);
        var artifactName = SanitizeArtifactName(spec.ArtifactName);
        // Microsoft.PowerShell.SDK carries deeply nested content files. Keeping the disposable
        // generated project below the durable output directory can exceed MAX_PATH on Windows
        // even when the user's final artifact path is otherwise reasonable.
        var workspace = Path.Combine(Path.GetTempPath(), "PowerForge", "ps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var result = new PowerShellCompilationBuildResult { BuildWorkspace = spec.KeepBuildWorkspace ? workspace : null };

        try
        {
            var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(spec.SourcePath, spec.Mode));
            if (plan.ParseErrorFiles > 0)
                throw new InvalidOperationException("PowerShell source contains parser errors; no artifact was produced.");

            var publishDirectory = Path.Combine(workspace, "publish");
            Directory.CreateDirectory(publishDirectory);
            PowerShellTypedCompilationResult? typed = null;
            string projectPath;
            bool requiresPowerShellRuntime;
            bool usesPowerShellRuntimeFallback;
            int compiledUnits;
            if (spec.Kind == PowerShellCompilationArtifactKind.Executable && spec.Mode == PowerShellCompilationMode.Strict)
            {
                var executable = PowerShellTypedExecutableEmitter.Emit(spec.SourcePath, plan);
                File.WriteAllText(Path.Combine(workspace, "CompiledPowerShellScript.cs"), executable.CompiledSource, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.WriteAllText(Path.Combine(workspace, "Program.cs"), executable.ProgramSource, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                projectPath = Path.Combine(workspace, artifactName + ".csproj");
                File.WriteAllText(
                    projectPath,
                    ReadTemplate(TypedExecutableProjectTemplate)
                        .Replace("{{TARGET_FRAMEWORK}}", EscapeXml(spec.TargetFramework))
                        .Replace("{{ARTIFACT_NAME}}", EscapeXml(artifactName))
                        .Replace("{{SINGLE_FILE}}", spec.SingleFile ? "true" : "false")
                        .Replace("{{SELF_CONTAINED}}", spec.SelfContained ? "true" : "false")
                        .Replace("{{PUBLISH_TRIMMED}}", spec.Optimization != PowerShellCompilationExecutableOptimization.None ? "true" : "false")
                        .Replace("{{PUBLISH_AOT}}", spec.Optimization == PowerShellCompilationExecutableOptimization.NativeAot ? "true" : "false"),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                requiresPowerShellRuntime = false;
                usesPowerShellRuntimeFallback = false;
                compiledUnits = 1;
            }
            else if (spec.Kind is PowerShellCompilationArtifactKind.Library or PowerShellCompilationArtifactKind.BinaryModule)
            {
                if (spec.Mode == PowerShellCompilationMode.Package)
                    throw new InvalidOperationException("DLL artifacts require Hybrid or Strict mode because they contain genuinely typed methods.");
                typed = new PowerShellTypedCompilationTranspiler().Transpile(
                    spec.SourcePath,
                    "PowerForge.Compiled",
                    PowerShellCSharpMethodEmitter.SanitizeIdentifier(artifactName) + "Methods");
                if (typed.Methods.Length == 0)
                    throw new InvalidOperationException("No PowerShell functions were eligible for typed CLR compilation.");
                if (spec.Mode == PowerShellCompilationMode.Strict && typed.Diagnostics.Length > 0)
                    throw new InvalidOperationException($"Strict mode rejected {typed.Diagnostics.Length} compilation blocker(s).");
                File.WriteAllText(Path.Combine(workspace, "CompiledPowerShell.cs"), typed.SourceCode, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                if (spec.Kind == PowerShellCompilationArtifactKind.BinaryModule)
                {
                    var exportContract = PowerShellModuleExportContract.TryRead(spec.SourcePath);
                    var exportedFunctions = exportContract?.SelectFunctions(typed.Methods.Select(static method => method.SourceName));
                    File.WriteAllText(
                        Path.Combine(workspace, "CompiledCmdlets.cs"),
                        GenerateBinaryCmdletSource(typed, exportedFunctions),
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }
                projectPath = Path.Combine(workspace, artifactName + ".csproj");
                var projectTemplate = spec.Kind == PowerShellCompilationArtifactKind.BinaryModule
                    ? ReadTemplate(BinaryModuleProjectTemplate).Replace("{{POWERSHELL_REFERENCE}}", GetPowerShellReference(spec.TargetFramework))
                    : ReadTemplate(TypedProjectTemplate);
                File.WriteAllText(
                    projectPath,
                    projectTemplate
                        .Replace("{{TARGET_FRAMEWORK}}", EscapeXml(spec.TargetFramework))
                        .Replace("{{ARTIFACT_NAME}}", EscapeXml(artifactName)),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                requiresPowerShellRuntime = spec.Kind == PowerShellCompilationArtifactKind.BinaryModule;
                usesPowerShellRuntimeFallback = spec.Kind == PowerShellCompilationArtifactKind.BinaryModule && typed.Methods.Length != plan.TotalUnits;
                compiledUnits = typed.Methods.Length;
            }
            else
            {
                File.WriteAllText(
                    Path.Combine(workspace, "Source.ps1"),
                    GeneratePackagedScript(spec.SourcePath),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.WriteAllText(
                    Path.Combine(workspace, "Program.cs"),
                    ReadTemplate(PackagedProgramTemplate)
                        .Replace("{{SWITCH_PARAMETERS}}", GenerateSwitchParameterInitializer(spec.SourcePath)),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                projectPath = Path.Combine(workspace, artifactName + ".csproj");
                File.WriteAllText(
                    projectPath,
                    ReadTemplate(PackagedProjectTemplate)
                        .Replace("{{TARGET_FRAMEWORK}}", EscapeXml(spec.TargetFramework))
                        .Replace("{{ARTIFACT_NAME}}", EscapeXml(artifactName))
                        .Replace("{{SINGLE_FILE}}", spec.SingleFile ? "true" : "false")
                        .Replace("{{SELF_CONTAINED}}", spec.SelfContained ? "true" : "false")
                        .Replace("{{POWERSHELL_SDK_VERSION}}", GetPowerShellSdkVersion(spec.TargetFramework)),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                requiresPowerShellRuntime = true;
                usesPowerShellRuntimeFallback = true;
                compiledUnits = 0;
            }

            var runtimeIdentifier = ResolveRuntimeIdentifier(spec);
            var process = RunDotNetBuild(spec, projectPath, publishDirectory, runtimeIdentifier);
            result.BuildOutput = BoundOutput(process.Output);
            if (process.TimedOut)
                throw new TimeoutException($"Generated .NET build exceeded {spec.TimeoutSeconds} seconds.");
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Generated .NET build failed with exit code {process.ExitCode}.");

            var artifactStagingDirectory = PowerShellArtifactSetPublisher.CreateStagingDirectory(spec.OutputDirectory, artifactName);
            try
            {
                var stagedArtifact = CopyArtifact(spec, artifactName, publishDirectory, typed, usesPowerShellRuntimeFallback, artifactStagingDirectory);
                var signing = PowerShellCompilationArtifactSigner.Sign(spec, stagedArtifact.Files);
                if (signing is not null)
                {
                    foreach (var file in stagedArtifact.Files)
                    {
                        file.Sha256 = ComputeSha256(file.Path);
                        file.SizeBytes = new FileInfo(file.Path).Length;
                    }
                }
                var artifactPath = PowerShellArtifactSetPublisher.RebasePath(stagedArtifact.PrimaryPath, artifactStagingDirectory, spec.OutputDirectory);
                var nonCompiledUnits = Math.Max(0, plan.TotalUnits - compiledUnits);
                var fallbackUnits = usesPowerShellRuntimeFallback ? nonCompiledUnits : 0;
                var omittedUnits = spec.Kind == PowerShellCompilationArtifactKind.Library ? nonCompiledUnits : 0;
                var diagnostics = typed?.Diagnostics ?? plan.Files
                    .SelectMany(static file => file.Diagnostics.Concat(file.Units.SelectMany(static unit => unit.Diagnostics)))
                    .ToArray();
                var manifest = new PowerShellCompilationArtifactManifest
                {
                    ArtifactName = artifactName,
                    Kind = spec.Kind,
                    Mode = spec.Mode,
                    SourcePath = spec.SourcePath,
                    TargetFramework = spec.TargetFramework,
                    RuntimeIdentifier = runtimeIdentifier,
                    RequiresPowerShellRuntime = requiresPowerShellRuntime,
                    UsesPowerShellRuntimeFallback = usesPowerShellRuntimeFallback,
                    SelfContained = spec.SelfContained,
                    SingleFile = spec.Kind == PowerShellCompilationArtifactKind.Executable && spec.SingleFile,
                    Optimization = spec.Optimization,
                    CompiledMethods = compiledUnits,
                    RuntimeFallbackUnits = fallbackUnits,
                    OmittedUnits = omittedUnits,
                    CompilationCoveragePercentage = plan.TotalUnits == 0 ? 0 : compiledUnits * 100d / plan.TotalUnits,
                    ArtifactPath = artifactPath,
                    ArtifactSha256 = ComputeSha256(stagedArtifact.PrimaryPath),
                    ArtifactSizeBytes = new FileInfo(stagedArtifact.PrimaryPath).Length,
                    AuthenticodeSigned = signing is not null,
                    SigningCertificateThumbprint = signing?.CertificateThumbprint,
                    AuthenticodeSignedFiles = signing?.SignedFiles ?? 0,
                    Files = PowerShellArtifactSetPublisher.RebaseFiles(stagedArtifact.Files, artifactStagingDirectory, spec.OutputDirectory),
                    Diagnostics = diagnostics
                };
                var manifestPath = Path.Combine(spec.OutputDirectory, artifactName + ".powerforge-compilation.json");
                WriteManifest(Path.Combine(artifactStagingDirectory, Path.GetFileName(manifestPath)), manifest);
                PowerShellArtifactSetPublisher.Commit(artifactStagingDirectory, spec.OutputDirectory, artifactName);

                result.Succeeded = true;
                result.ArtifactPath = artifactPath;
                result.ManifestPath = manifestPath;
                result.Manifest = manifest;
                return result;
            }
            finally
            {
                PowerShellArtifactSetPublisher.TryDeleteDirectory(artifactStagingDirectory);
            }
        }
        catch (Exception ex)
        {
            result.Succeeded = false;
            result.Error = ex.Message;
            return result;
        }
        finally
        {
            if (!spec.KeepBuildWorkspace)
            {
                try { Directory.Delete(workspace, recursive: true); } catch { }
            }
        }
    }

    private static void ValidateSpec(PowerShellCompilationBuildSpec spec)
    {
        if (spec.Mode == PowerShellCompilationMode.Analyze)
            throw new ArgumentException("Analyze mode reports eligibility and does not produce artifacts. Use the analyzer API or CLI analyze command.", nameof(spec));
        if (!File.Exists(spec.SourcePath))
            throw new FileNotFoundException("PowerShell source file was not found.", spec.SourcePath);
        var extension = Path.GetExtension(spec.SourcePath);
        if (!extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("PowerShell artifacts accept .ps1 and .psm1 source files.", nameof(spec));
        if (spec.TimeoutSeconds < 1)
            throw new ArgumentOutOfRangeException(nameof(spec), "Build timeout must be positive.");
        if (spec.SignArtifact && string.IsNullOrWhiteSpace(spec.TimeStampServer))
            throw new ArgumentException("Signing requires an RFC3161 timestamp server URL.", nameof(spec));
        if (spec.SigningTimeoutSeconds < 1)
            throw new ArgumentOutOfRangeException(nameof(spec), "Signing timeout must be positive.");
        if (spec.Kind == PowerShellCompilationArtifactKind.Executable && !spec.TargetFramework.Equals("net8.0", StringComparison.OrdinalIgnoreCase) && !spec.TargetFramework.Equals("net10.0", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Executables currently target net8.0 or net10.0.", nameof(spec));
        if (spec.Optimization != PowerShellCompilationExecutableOptimization.None)
        {
            if (spec.Kind != PowerShellCompilationArtifactKind.Executable || spec.Mode != PowerShellCompilationMode.Strict)
                throw new ArgumentException("Executable optimization is supported only for Strict genuinely typed executables.", nameof(spec));
            if (!spec.SelfContained || string.IsNullOrWhiteSpace(spec.RuntimeIdentifier) || !spec.SingleFile)
                throw new ArgumentException("Trimmed and NativeAot executables require SelfContained, RuntimeIdentifier, and SingleFile.", nameof(spec));
        }
        if (spec.Kind != PowerShellCompilationArtifactKind.Executable &&
            !spec.TargetFramework.Equals("net472", StringComparison.OrdinalIgnoreCase) &&
            !spec.TargetFramework.Equals("net8.0", StringComparison.OrdinalIgnoreCase) &&
            !spec.TargetFramework.Equals("net10.0", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Typed libraries and binary modules currently target net472, net8.0, or net10.0.", nameof(spec));
    }

    private static GeneratedBuildProcessResult RunDotNetBuild(
        PowerShellCompilationBuildSpec spec,
        string projectPath,
        string publishDirectory,
        string? runtimeIdentifier)
    {
        var arguments = new List<string>
        {
            spec.Kind == PowerShellCompilationArtifactKind.Executable ? "publish" : "build",
            projectPath,
            "--configuration", "Release",
            "--output", publishDirectory,
            "--nologo",
            "--verbosity", "minimal"
        };
        if (spec.Kind == PowerShellCompilationArtifactKind.Executable && !string.IsNullOrWhiteSpace(runtimeIdentifier))
        {
            arguments.Add("--runtime");
            arguments.Add(runtimeIdentifier!);
        }

        var run = new ProcessRunner().RunAsync(new ProcessRunRequest(
                "dotnet",
                Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory(),
                arguments,
                TimeSpan.FromSeconds(spec.TimeoutSeconds)))
            .GetAwaiter()
            .GetResult();
        var output = string.IsNullOrWhiteSpace(run.StdErr)
            ? run.StdOut
            : run.StdOut + Environment.NewLine + run.StdErr;
        return new GeneratedBuildProcessResult(run.ExitCode, output, run.TimedOut);
    }

    private static CopiedArtifact CopyArtifact(
        PowerShellCompilationBuildSpec spec,
        string artifactName,
        string publishDirectory,
        PowerShellTypedCompilationResult? typed,
        bool usesPowerShellRuntimeFallback,
        string outputDirectory)
    {
        if (spec.Kind is PowerShellCompilationArtifactKind.Library or PowerShellCompilationArtifactKind.BinaryModule)
        {
            var source = Path.Combine(publishDirectory, artifactName + ".dll");
            if (!File.Exists(source)) throw new FileNotFoundException("Generated library was not found.", source);

            if (spec.Kind == PowerShellCompilationArtifactKind.BinaryModule && usesPowerShellRuntimeFallback)
                return CopyHybridModule(spec, artifactName, source, typed ?? throw new InvalidOperationException("Typed module metadata was not available."), outputDirectory);

            if (spec.Kind == PowerShellCompilationArtifactKind.BinaryModule && HasSiblingModuleManifest(spec.SourcePath))
                return CopyStrictModuleWithManifest(spec, artifactName, source, typed ?? throw new InvalidOperationException("Typed module metadata was not available."), outputDirectory);

            var target = Path.Combine(outputDirectory, artifactName + ".dll");
            File.Copy(source, target, overwrite: true);
            return CreateCopiedArtifactWithSymbols(source, target, "Primary");
        }

        var executableFileName = GetExecutableFileName(artifactName, spec.RuntimeIdentifier);
        var executable = Path.Combine(publishDirectory, executableFileName);
        if (!File.Exists(executable)) throw new FileNotFoundException("Generated executable was not found.", executable);
        if (spec.SingleFile)
        {
            var target = Path.Combine(outputDirectory, executableFileName);
            File.Copy(executable, target, overwrite: true);
            return CreateCopiedArtifactWithSymbols(executable, target, "Primary");
        }

        var targetDirectory = Path.Combine(outputDirectory, artifactName);
        Directory.CreateDirectory(targetDirectory);
        foreach (var directory in Directory.EnumerateDirectories(publishDirectory, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(targetDirectory, FrameworkCompatibility.GetRelativePath(publishDirectory, directory)));
        foreach (var file in Directory.EnumerateFiles(publishDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = FrameworkCompatibility.GetRelativePath(publishDirectory, file);
            var target = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? targetDirectory);
            File.Copy(file, target, overwrite: true);
        }
        var primaryPath = Path.Combine(targetDirectory, executableFileName);
        var files = Directory.EnumerateFiles(targetDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => CreateArtifactFile(
                path,
                path.Equals(primaryPath, StringComparison.OrdinalIgnoreCase) ? "Primary" : "RuntimeDependency"))
            .ToArray();
        return new CopiedArtifact(primaryPath, files);
    }

    internal static string GetExecutableFileName(string artifactName, string? runtimeIdentifier)
    {
        var targetsWindows = string.IsNullOrWhiteSpace(runtimeIdentifier)
            ? RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            : runtimeIdentifier!.StartsWith("win", StringComparison.OrdinalIgnoreCase);
        return targetsWindows ? artifactName + ".exe" : artifactName;
    }

    private static CopiedArtifact CopyHybridModule(
        PowerShellCompilationBuildSpec spec,
        string artifactName,
        string compiledAssembly,
        PowerShellTypedCompilationResult typed,
        string outputDirectory)
    {
        var moduleDirectory = Path.Combine(outputDirectory, artifactName);
        Directory.CreateDirectory(moduleDirectory);
        var assemblyPath = Path.Combine(moduleDirectory, artifactName + ".dll");
        var modulePath = Path.Combine(moduleDirectory, artifactName + ".psm1");
        File.Copy(compiledAssembly, assemblyPath, overwrite: true);
        var files = new List<PowerShellCompilationArtifactFile>();
        File.WriteAllText(
            modulePath,
            GenerateHybridModuleScript(spec.SourcePath, Path.GetFileName(assemblyPath), typed),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        files.Add(CreateArtifactFile(modulePath, "PrimaryModule"));
        files.Add(CreateArtifactFile(assemblyPath, "TypedAssembly"));
        CopyDebugSymbolsIfPresent(compiledAssembly, assemblyPath, files);
        var manifestFiles = PowerShellCompiledModuleManifest.Create(
            spec.SourcePath,
            moduleDirectory,
            artifactName,
            Path.GetFileName(modulePath),
            typed);
        if (manifestFiles is null)
            return new CopiedArtifact(modulePath, files.ToArray());
        foreach (var manifestFile in manifestFiles)
            files.Add(CreateArtifactFile(manifestFile, manifestFile.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase) ? "PrimaryModuleManifest" : "ModuleDependency"));
        var manifestPath = manifestFiles.First(path => path.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase));
        return new CopiedArtifact(manifestPath, files.ToArray());
    }

    private static CopiedArtifact CopyStrictModuleWithManifest(
        PowerShellCompilationBuildSpec spec,
        string artifactName,
        string compiledAssembly,
        PowerShellTypedCompilationResult typed,
        string outputDirectory)
    {
        var moduleDirectory = Path.Combine(outputDirectory, artifactName);
        Directory.CreateDirectory(moduleDirectory);
        var assemblyPath = Path.Combine(moduleDirectory, artifactName + ".dll");
        File.Copy(compiledAssembly, assemblyPath, overwrite: true);
        var files = new List<PowerShellCompilationArtifactFile> { CreateArtifactFile(assemblyPath, "TypedAssembly") };
        CopyDebugSymbolsIfPresent(compiledAssembly, assemblyPath, files);
        var manifestFiles = PowerShellCompiledModuleManifest.Create(
            spec.SourcePath,
            moduleDirectory,
            artifactName,
            Path.GetFileName(assemblyPath),
            typed) ?? throw new InvalidOperationException("The sibling module manifest was not available during artifact publication.");
        foreach (var manifestFile in manifestFiles)
            files.Add(CreateArtifactFile(manifestFile, manifestFile.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase) ? "PrimaryModuleManifest" : "ModuleDependency"));
        var manifestPath = manifestFiles.First(path => path.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase));
        return new CopiedArtifact(manifestPath, files.ToArray());
    }

    private static string GeneratePackagedScript(string sourcePath)
    {
        Token[] tokens;
        ParseError[] errors;
        var ast = Parser.ParseFile(sourcePath, out tokens, out errors);
        if (errors.Length > 0)
            throw new InvalidOperationException("Packaged script could not be parsed while preserving exit-code semantics.");

        var source = new StringBuilder(File.ReadAllText(sourcePath));
        var exits = ast.FindAll(static node => node is ExitStatementAst, searchNestedScriptBlocks: true)
            .Cast<ExitStatementAst>()
            .ToArray();
        if (exits.Length > 0 && ast.FindAll(static node => node is TrapStatementAst, searchNestedScriptBlocks: true).Any())
            throw new InvalidOperationException("Packaged scripts that combine exit with trap are not supported because exception instrumentation would change trap semantics.");
        foreach (var exit in exits)
        {
            for (var parent = exit.Parent; parent is not null && !ReferenceEquals(parent, ast); parent = parent.Parent)
            {
                if (parent is FunctionDefinitionAst or ScriptBlockExpressionAst ||
                    parent is ScriptBlockAst scriptBlock && !ReferenceEquals(scriptBlock, ast) ||
                    parent is TryStatementAst tryStatement && tryStatement.CatchClauses.Count > 0)
                    throw new InvalidOperationException($"exit at line {exit.Extent.StartLineNumber} cannot be packaged safely because exception instrumentation would change nested or catch behavior.");
            }
        }

        foreach (var exit in exits.OrderByDescending(static exit => exit.Extent.StartOffset))
        {
            var expression = exit.Pipeline?.Extent.Text;
            var exitCode = string.IsNullOrWhiteSpace(expression) ? "0" : "[int](" + expression + ")";
            var replacement = "throw [PowerForge.Compiled.PowerForgeScriptExitException]::new(" + exitCode + ")";
            source.Remove(exit.Extent.StartOffset, exit.Extent.EndOffset - exit.Extent.StartOffset);
            source.Insert(exit.Extent.StartOffset, replacement);
        }
        var prologueEndOffset = ast.ParamBlock?.Extent.EndOffset ?? 0;
        foreach (var usingStatement in ast.FindAll(static node => node is UsingStatementAst, searchNestedScriptBlocks: false).Cast<UsingStatementAst>())
            prologueEndOffset = Math.Max(prologueEndOffset, usingStatement.Extent.EndOffset);
        var pathSemantics = new StringBuilder();
        if (prologueEndOffset > 0 && source[prologueEndOffset - 1] is not '\r' and not '\n') pathSemantics.AppendLine();
        pathSemantics.AppendLine("$script:PSCommandPath = [System.Environment]::ProcessPath");
        pathSemantics.AppendLine("$script:PSScriptRoot = [System.IO.Path]::GetDirectoryName($script:PSCommandPath)");
        source.Insert(prologueEndOffset, pathSemantics.ToString());
        return source.ToString();
    }

    private static string GenerateSwitchParameterInitializer(string sourcePath)
    {
        Token[] tokens;
        ParseError[] errors;
        var ast = Parser.ParseFile(sourcePath, out tokens, out errors);
        if (errors.Length > 0)
            throw new InvalidOperationException("Packaged script parameters could not be parsed for native argument binding.");
        var switchParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in ast.ParamBlock?.Parameters.Where(static parameter => parameter.StaticType == typeof(System.Management.Automation.SwitchParameter)) ?? Array.Empty<ParameterAst>())
        {
            switchParameters.Add(parameter.Name.VariablePath.UserPath);
            foreach (var alias in parameter.Attributes.OfType<AttributeAst>().Where(static attribute => IsAttributeNamed(attribute, "Alias")))
            foreach (var value in alias.PositionalArguments.OfType<StringConstantExpressionAst>())
                switchParameters.Add(value.Value);
        }
        if (ast.ParamBlock?.Attributes.OfType<AttributeAst>().Any(static attribute => IsAttributeNamed(attribute, "CmdletBinding")) == true)
            switchParameters.UnionWith(new[] { "Verbose", "Debug", "WhatIf", "Confirm" });
        return string.Join(", ", switchParameters
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => "\"" + EscapeCSharpString(name) + "\"")
            .ToArray());
    }

    private static bool IsAttributeNamed(AttributeAst attribute, string name)
    {
        var fullName = attribute.TypeName.FullName;
        return fullName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
               fullName.Equals(name + "Attribute", StringComparison.OrdinalIgnoreCase) ||
               fullName.EndsWith("." + name + "Attribute", StringComparison.OrdinalIgnoreCase);
    }

    private static string GenerateHybridModuleScript(
        string sourcePath,
        string assemblyFileName,
        PowerShellTypedCompilationResult typed)
    {
        Token[] tokens;
        ParseError[] errors;
        var ast = Parser.ParseFile(sourcePath, out tokens, out errors);
        if (errors.Length > 0)
            throw new InvalidOperationException("Hybrid module source could not be parsed while composing fallback code.");

        var compiled = new HashSet<string>(
            typed.Methods.Select(static method => method.SourceName + "\0" + method.SourceLine),
            StringComparer.OrdinalIgnoreCase);
        var prologueEndOffset = ast.ParamBlock?.Extent.EndOffset ?? 0;
        foreach (var usingStatement in ast.FindAll(static node => node is UsingStatementAst, searchNestedScriptBlocks: false).Cast<UsingStatementAst>())
            prologueEndOffset = Math.Max(prologueEndOffset, usingStatement.Extent.EndOffset);
        var functions = ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
            .Cast<FunctionDefinitionAst>()
            .OrderByDescending(static function => function.Extent.StartOffset)
            .ToArray();
        var source = new StringBuilder(File.ReadAllText(sourcePath));
        var exportContract = PowerShellModuleExportContract.TryRead(ast);
        var removals = new List<(int Start, int Length)>();
        foreach (var function in functions)
        {
            if (!compiled.Contains(function.Name + "\0" + function.Extent.StartLineNumber))
                continue;
            if (function.Extent.StartOffset < prologueEndOffset)
                throw new InvalidOperationException($"Compiled function '{function.Name}' overlaps the module prologue and cannot be composed safely.");
            removals.Add((function.Extent.StartOffset, function.Extent.EndOffset - function.Extent.StartOffset));
        }
        if (exportContract is not null)
            removals.AddRange(exportContract.Commands.Select(static command =>
                (command.Extent.StartOffset, command.Extent.EndOffset - command.Extent.StartOffset)));
        foreach (var removal in removals.OrderByDescending(static removal => removal.Start))
            source.Remove(removal.Start, removal.Length);

        var fallbackFunctions = functions
            .Where(function => !compiled.Contains(function.Name + "\0" + function.Extent.StartLineNumber))
            .OrderBy(static function => function.Extent.StartOffset)
            .Select(static function => function.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var compiledCmdlets = typed.Methods
            .Select(static method => method.SourceName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var exportedFallbackFunctions = exportContract?.SelectFunctions(fallbackFunctions) ?? fallbackFunctions;
        var exportedCompiledCmdlets = exportContract?.SelectFunctions(compiledCmdlets) ?? compiledCmdlets;
        var additionalCmdlets = exportContract?.Cmdlets ?? Array.Empty<string>();
        var aliases = exportContract?.Aliases ?? new[] { "*" };
        var variables = exportContract?.Variables ?? Array.Empty<string>();
        var import = new StringBuilder();
        if (prologueEndOffset > 0 && source[prologueEndOffset - 1] is not '\r' and not '\n') import.AppendLine();
        import.AppendLine("# Generated by PowerForge hybrid PowerShell compilation.");
        import.AppendLine("Import-Module -Name (Join-Path -Path $PSScriptRoot -ChildPath '" + EscapePowerShellSingleQuotedString(assemblyFileName) + "') -Force -ErrorAction Stop");
        import.AppendLine();
        source.Insert(prologueEndOffset, import.ToString());
        var builder = new StringBuilder(source.ToString());
        if (source.Length > 0 && source[source.Length - 1] != '\n') builder.AppendLine();
        builder.AppendLine();
        builder.Append("Export-ModuleMember -Function @(").Append(JoinPowerShellNames(exportedFallbackFunctions))
            .Append(") -Cmdlet @(").Append(JoinPowerShellNames(exportedCompiledCmdlets.Concat(additionalCmdlets).Distinct(StringComparer.OrdinalIgnoreCase)))
            .Append(") -Alias @(").Append(JoinPowerShellNames(aliases)).Append(')');
        if (variables.Length > 0)
            builder.Append(" -Variable @(").Append(JoinPowerShellNames(variables)).Append(')');
        builder.AppendLine();
        return builder.ToString();
    }

    private static bool HasSiblingModuleManifest(string sourcePath)
        => Path.GetExtension(sourcePath).Equals(".psm1", StringComparison.OrdinalIgnoreCase) &&
           File.Exists(Path.ChangeExtension(sourcePath, ".psd1"));

    private static string JoinPowerShellNames(IEnumerable<string> names)
        => string.Join(", ", names.Select(name => "'" + EscapePowerShellSingleQuotedString(name) + "'"));

    private static string EscapePowerShellSingleQuotedString(string value)
        => value.Replace("'", "''");

    private static CopiedArtifact CreateCopiedArtifactWithSymbols(string sourcePath, string targetPath, string role)
    {
        var files = new List<PowerShellCompilationArtifactFile> { CreateArtifactFile(targetPath, role) };
        CopyDebugSymbolsIfPresent(sourcePath, targetPath, files);
        return new CopiedArtifact(targetPath, files.ToArray());
    }

    private static void CopyDebugSymbolsIfPresent(
        string sourceArtifact,
        string targetArtifact,
        ICollection<PowerShellCompilationArtifactFile> files)
    {
        var sourcePdb = Path.ChangeExtension(sourceArtifact, ".pdb");
        if (!File.Exists(sourcePdb)) return;
        var targetPdb = Path.ChangeExtension(targetArtifact, ".pdb");
        File.Copy(sourcePdb, targetPdb, overwrite: true);
        files.Add(CreateArtifactFile(targetPdb, "DebugSymbols"));
    }

    private static PowerShellCompilationArtifactFile CreateArtifactFile(string path, string role)
        => new() { Path = path, Role = role, Sha256 = ComputeSha256(path), SizeBytes = new FileInfo(path).Length };

    private static string? ResolveRuntimeIdentifier(PowerShellCompilationBuildSpec spec)
    {
        if (spec.Kind != PowerShellCompilationArtifactKind.Executable)
            return null;
        if (!string.IsNullOrWhiteSpace(spec.RuntimeIdentifier))
            return spec.RuntimeIdentifier;
        if (!spec.SingleFile && !spec.SelfContained)
            return null;

        var prefix = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "linux";
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "x64"
        };
        return prefix + "-" + architecture;
    }

    private static string GetPowerShellSdkVersion(string targetFramework)
        => targetFramework.Equals("net10.0", StringComparison.OrdinalIgnoreCase) ? "7.6.4" : "7.4.18";

    private static string GetPowerShellReference(string targetFramework)
        => targetFramework.Equals("net472", StringComparison.OrdinalIgnoreCase)
            ? "<PackageReference Include=\"Microsoft.PowerShell.5.ReferenceAssemblies\" Version=\"1.1.0\" PrivateAssets=\"all\" />"
            : $"<PackageReference Include=\"Microsoft.PowerShell.SDK\" Version=\"{GetPowerShellSdkVersion(targetFramework)}\" PrivateAssets=\"all\" ExcludeAssets=\"runtime\" />";

    private static string GenerateBinaryCmdletSource(PowerShellTypedCompilationResult typed, string[]? exportedFunctions = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("using System.Management.Automation;");
        builder.AppendLine();
        builder.AppendLine($"namespace {typed.NamespaceName};");
        builder.AppendLine();
        var selected = exportedFunctions is null
            ? null
            : exportedFunctions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var method in typed.Methods.Where(method => selected is null || selected.Contains(method.SourceName)))
        {
            var separator = method.SourceName.IndexOf('-');
            if (separator < 1 || separator == method.SourceName.Length - 1)
                throw new InvalidOperationException($"Function '{method.SourceName}' cannot be exported as a binary cmdlet because it does not use Verb-Noun naming.");
            var verb = method.SourceName.Substring(0, separator);
            var noun = method.SourceName.Substring(separator + 1);
            var className = PowerShellCSharpMethodEmitter.SanitizeIdentifier(verb + noun + "Command");
            var commonParameter = method.Parameters.FirstOrDefault(parameter => CommonCmdletParameterNames.Contains(parameter.Name));
            if (commonParameter is not null)
                throw new InvalidOperationException($"Function '{method.SourceName}' parameter '${commonParameter.Name}' collides with a PowerShell common parameter and cannot be exported as a binary cmdlet.");
            builder.AppendLine($"[Cmdlet(\"{EscapeCSharpString(verb)}\", \"{EscapeCSharpString(noun)}\")]");
            if (!method.ReturnType.Equals(typeof(void).FullName, StringComparison.Ordinal))
                builder.AppendLine($"[OutputType(typeof({GetGeneratedTypeName(method.ReturnType)}))]");
            builder.AppendLine($"public sealed class {className} : PSCmdlet");
            builder.AppendLine("{");
            for (var index = 0; index < method.Parameters.Length; index++)
            {
                var parameter = method.Parameters[index];
                builder.AppendLine($"    [Parameter(Position = {index})]");
                builder.AppendLine($"    public {GetGeneratedTypeName(parameter.TypeName)} {PowerShellCSharpMethodEmitter.SanitizeIdentifier(parameter.Name)} {{ get; set; }}{(parameter.TypeName == typeof(string).FullName ? " = string.Empty;" : string.Empty)}");
                builder.AppendLine();
            }
            builder.AppendLine("    protected override void ProcessRecord()");
            builder.AppendLine("    {");
            var arguments = string.Join(", ", method.Parameters.Select(parameter => PowerShellCSharpMethodEmitter.SanitizeIdentifier(parameter.Name)));
            var invocation = $"{typed.TypeName}.{method.GeneratedName}({arguments})";
            if (method.ReturnType.Equals(typeof(void).FullName, StringComparison.Ordinal))
                builder.AppendLine($"        {invocation};");
            else if (method.ReturnType.EndsWith("[]", StringComparison.Ordinal))
                builder.AppendLine($"        WriteObject({invocation}, enumerateCollection: true);");
            else
                builder.AppendLine($"        WriteObject({invocation});");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static string GetGeneratedTypeName(string fullName)
    {
        if (fullName.EndsWith("[]", StringComparison.Ordinal))
            return GetGeneratedTypeName(fullName.Substring(0, fullName.Length - 2)) + "[]";
        if (fullName == typeof(void).FullName) return "void";
        if (fullName == typeof(bool).FullName) return "bool";
        if (fullName == typeof(byte).FullName) return "byte";
        if (fullName == typeof(sbyte).FullName) return "sbyte";
        if (fullName == typeof(short).FullName) return "short";
        if (fullName == typeof(ushort).FullName) return "ushort";
        if (fullName == typeof(int).FullName) return "int";
        if (fullName == typeof(uint).FullName) return "uint";
        if (fullName == typeof(long).FullName) return "long";
        if (fullName == typeof(ulong).FullName) return "ulong";
        if (fullName == typeof(float).FullName) return "float";
        if (fullName == typeof(double).FullName) return "double";
        if (fullName == typeof(decimal).FullName) return "decimal";
        if (fullName == typeof(char).FullName) return "char";
        if (fullName == typeof(string).FullName) return "string";
        return "global::" + fullName.Replace('+', '.');
    }

    private static string EscapeCSharpString(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string ReadTemplate(string resourceName)
    {
        using var stream = typeof(PowerShellCompilationArtifactBuilder).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded compilation template '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void WriteManifest(string path, PowerShellCompilationArtifactManifest manifest)
    {
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter());
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, options), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        return string.Concat(algorithm.ComputeHash(stream).Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static string SanitizeArtifactName(string value)
    {
        var sanitized = new string(value.Trim().Select(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_').ToArray());
        if (string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or "..")
            throw new ArgumentException("Artifact name does not contain a usable file name.", nameof(value));
        return sanitized;
    }

    private static string EscapeXml(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;

    private static string BoundOutput(string output)
        => output.Length <= MaximumBuildOutputLength ? output : output.Substring(output.Length - MaximumBuildOutputLength);

    private sealed class CopiedArtifact
    {
        internal CopiedArtifact(string primaryPath, PowerShellCompilationArtifactFile[] files)
        {
            PrimaryPath = primaryPath;
            Files = files;
        }

        internal string PrimaryPath { get; }
        internal PowerShellCompilationArtifactFile[] Files { get; }
    }

    private sealed class GeneratedBuildProcessResult
    {
        internal GeneratedBuildProcessResult(int exitCode, string output, bool timedOut)
        {
            ExitCode = exitCode;
            Output = output;
            TimedOut = timedOut;
        }

        internal int ExitCode { get; }
        internal string Output { get; }
        internal bool TimedOut { get; }
    }
}
