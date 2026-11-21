using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Discovers and stages contained literal dot-sourced files required by retained hybrid module source.
/// </summary>
internal static class PowerShellHybridDependencyResolver
{
    internal static PowerShellStagedModuleFile[] CopyDependencies(
        string sourcePath,
        string moduleDirectory,
        IEnumerable<string>? additionalEntryPaths = null,
        Func<string, string?>? contentTransformer = null,
        IEnumerable<string>? explicitDependencyPaths = null,
        IReadOnlyCollection<PowerShellConventionalLoaderIdentity>? conventionalLoaders = null,
        Action<string, string>? verifyCopiedInput = null,
        PowerShellSourceSnapshot? snapshot = null,
        string? deliveryProfile = null)
    {
        var sourceRoot = Path.GetFullPath(Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory());
        var comparer = PowerShellCompilationPathSafety.PathComparer;
        var entryPaths = new HashSet<string>(
            new[] { sourcePath }.Concat(additionalEntryPaths ?? Array.Empty<string>()).Select(Path.GetFullPath),
            comparer);
        var discoveryEntries = (additionalEntryPaths ?? Array.Empty<string>())
            .Concat(explicitDependencyPaths ?? Array.Empty<string>());
        var copied = new List<PowerShellStagedModuleFile>();
        foreach (var dependency in DiscoverDependenciesCore(sourcePath, discoveryEntries, moduleScopeOnly: true, conventionalLoaders,
                     runtimeAlternatives: PowerShellModuleRuntimeAlternatives.Stage, snapshot: snapshot))
        {
            if (entryPaths.Contains(dependency))
                continue;
            var target = Path.GetFullPath(Path.Combine(moduleDirectory, FrameworkCompatibility.GetRelativePath(sourceRoot, dependency)));
            PowerShellCompilationPathSafety.EnsureContained(Path.GetFullPath(moduleDirectory), target, $"Dot-source target for '{dependency}' escapes the hybrid module staging root.");
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? moduleDirectory);
            var transformedContent = contentTransformer?.Invoke(dependency) ??
                (deliveryProfile is null ? null : snapshot?.GetDeliveryRewrite(dependency, deliveryProfile));
            if (File.Exists(target))
            {
                var matches = transformedContent is null
                    ? File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(dependency))
                    : File.ReadAllText(target).Equals(transformedContent, StringComparison.Ordinal);
                if (!matches)
                    throw new InvalidOperationException($"Dot-sourced hybrid module dependency '{dependency}' collides with a generated or manifest-staged artifact.");
            }
            else if (transformedContent is not null)
            {
                verifyCopiedInput?.Invoke(dependency, dependency);
                File.WriteAllText(target, transformedContent, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                copied.Add(new PowerShellStagedModuleFile(target, isGenerated: true));
            }
            else
            {
                File.Copy(dependency, target, overwrite: false);
                copied.Add(new PowerShellStagedModuleFile(target, isGenerated: false));
            }
            if (transformedContent is null)
                verifyCopiedInput?.Invoke(dependency, target);
        }
        return copied.ToArray();
    }

    internal static string[] DiscoverDependencies(
        string sourcePath,
        IEnumerable<string>? additionalEntryPaths = null,
        IReadOnlyCollection<PowerShellConventionalLoaderIdentity>? conventionalLoaders = null,
        bool allowDerivedPaths = false,
        PowerShellModuleRuntimeAlternatives runtimeAlternatives = PowerShellModuleRuntimeAlternatives.Reject,
        IReadOnlyDictionary<string, ParsedSourceDocument>? sourceDocuments = null,
        string? semanticProfileId = null,
        PowerShellSourceSnapshot? snapshot = null)
        => DiscoverDependenciesCore(sourcePath, additionalEntryPaths, moduleScopeOnly: false,
            conventionalLoaders, allowDerivedPaths, runtimeAlternatives, sourceDocuments, semanticProfileId, snapshot);

    internal static string[] DiscoverModuleScopeDependencies(string sourcePath)
        => DiscoverModuleScopeDependencies(sourcePath, null, null);

    internal static (string[] Compilation, string[] Runtime) DiscoverExecutableSources(
        string sourcePath, PowerShellCompilationMode mode, PowerShellSourceSnapshot snapshot)
    {
        var hosted = mode != PowerShellCompilationMode.Strict;
        var compiled = DiscoverDependencies(sourcePath, allowDerivedPaths: hosted, snapshot: snapshot);
        if (!hosted) return (compiled, compiled);
        // Called scripts own a child scope. Stage them, but do not add their functions to the caller's symbol table.
        var runtime = DiscoverDependenciesCore(sourcePath, null, moduleScopeOnly: false, conventionalLoaders: null,
            allowDerivedPaths: true, snapshot: snapshot, includeScriptCalls: true);
        return (compiled, runtime);
    }

    internal static string[] DiscoverModuleScopeDependencies(string sourcePath,
        IEnumerable<string>? additionalEntryPaths,
        IReadOnlyCollection<PowerShellConventionalLoaderIdentity>? conventionalLoaders,
        PowerShellModuleRuntimeAlternatives runtimeAlternatives = PowerShellModuleRuntimeAlternatives.Reject,
        PowerShellSourceSnapshot? snapshot = null)
        => DiscoverDependenciesCore(sourcePath, additionalEntryPaths, moduleScopeOnly: true,
            conventionalLoaders, runtimeAlternatives: runtimeAlternatives, snapshot: snapshot);

    internal static string[] DiscoverRetainedModuleAlternatives(string sourcePath, IEnumerable<string> compilationSources,
        PowerShellSourceSnapshot? snapshot = null)
    {
        // Traverse the alternate branches independently so a path reached by both
        // a static loader and a switch cannot disappear from the overlap check.
        var alternatives = DiscoverDependenciesCore(sourcePath, null, moduleScopeOnly: true, conventionalLoaders: null,
                runtimeAlternatives: PowerShellModuleRuntimeAlternatives.StageAlternativesOnly, snapshot: snapshot)
            .Where(path => !PowerShellCompilationPathSafety.PathEquals(path, Path.GetFullPath(sourcePath))).ToArray();
        var overlap = alternatives.Intersect(compilationSources, PowerShellCompilationPathSafety.PathComparer).FirstOrDefault();
        if (overlap is not null)
            throw new InvalidOperationException(
                $"Module switch alternative '{overlap}' cannot also be a compilation source. Its runtime file identity must be preserved.");
        return alternatives;
    }

    private static string[] DiscoverDependenciesCore(
        string sourcePath,
        IEnumerable<string>? additionalEntryPaths,
        bool moduleScopeOnly,
        IReadOnlyCollection<PowerShellConventionalLoaderIdentity>? conventionalLoaders,
        bool allowDerivedPaths = false,
        PowerShellModuleRuntimeAlternatives runtimeAlternatives = PowerShellModuleRuntimeAlternatives.Reject,
        IReadOnlyDictionary<string, ParsedSourceDocument>? sourceDocuments = null,
        string? semanticProfileId = null,
        PowerShellSourceSnapshot? snapshot = null,
        bool includeScriptCalls = false)
    {
        var primarySource = Path.GetFullPath(sourcePath);
        var sourceRoot = Path.GetFullPath(Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory());
        var discovered = new HashSet<string>(PowerShellCompilationPathSafety.PathComparer);
        var pending = new Queue<(string Path, bool RuntimeSelected)>();
        foreach (var entryPath in new[] { sourcePath }.Concat(additionalEntryPaths ?? Array.Empty<string>()))
        {
            var entry = Path.GetFullPath(entryPath);
            PowerShellCompilationPathSafety.EnsureContained(sourceRoot, entry, $"Hybrid module runtime source '{entry}' escapes the hybrid module source root.");
            if (!File.Exists(entry))
                throw new FileNotFoundException($"Hybrid module runtime source '{entry}' was not found.", entry);
            PowerShellCompilationPathSafety.EnsureNoLinks(sourceRoot, entry, $"Hybrid module runtime source '{entry}' traverses a symbolic link or junction, which is not allowed for hybrid staging.");
            if (discovered.Add(entry))
                pending.Enqueue((entry, RuntimeSelected: false));
        }

        while (pending.Count > 0)
        {
            var (current, runtimeSelected) = pending.Dequeue();
            Token[] tokens;
            ParseError[] errors;
            ScriptBlockAst ast;
            ParsedSourceDocument? document = null;
            sourceDocuments?.TryGetValue(current, out document);
            document ??= snapshot?.Get(current);
            if (document is null && semanticProfileId is not null)
                document = PowerShellSourceParser.ParseFile(current, sourceRoot, semanticProfileId);
            if (document is not null)
            {
                ast = document.SyntaxRoot;
                tokens = document.Tokens;
                errors = document.Errors;
            }
            else ast = Parser.ParseFile(current, out tokens, out errors);
            // A retained alternative can refer to types already declared in the importing module.
            // Its authored file is loaded in that scope, outside the CLR compilation source set.
            if (runtimeSelected)
                errors = errors.Where(static error => !IsStandaloneTypeResolutionError(error)).ToArray();
            if (errors.Length > 0)
                throw new InvalidOperationException(
                    $"Dot-sourced hybrid module dependency '{current}' could not be parsed: " +
                    string.Join("; ", errors.Take(3).Select(error =>
                        $"{error.Extent.StartLineNumber}:{error.Extent.StartColumnNumber} {error.Message}")));
            var dotSourceCommands = moduleScopeOnly
                ? PowerShellCompilationSourcePathPlanner.GetModuleScopeDotSources(ast)
                : ast.FindAll(
                        node => node is CommandAst command &&
                                (command.InvocationOperator == TokenKind.Dot ||
                                 includeScriptCalls && command.InvocationOperator == TokenKind.Ampersand),
                        searchNestedScriptBlocks: true)
                    .Cast<CommandAst>();
            foreach (var command in dotSourceCommands)
            {
                if (command.InvocationOperator == TokenKind.Ampersand)
                {
                    if (PowerShellCompilationSourcePathPlanner.PlanScriptCall(command) is not { } call) continue;
                    var relative = NormalizeRelativePath(call.RelativePath);
                    if (Path.IsPathRooted(relative) || WildcardPattern.ContainsWildcardCharacters(relative))
                        throw PowerShellCompilationSourceFailure.Create(
                            "A packaged script invocation needs a contained .ps1 target without wildcards.", current, call.Operand.Extent);
                    var calledScript = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!, relative));
                    PowerShellCompilationPathSafety.EnsureContained(sourceRoot, calledScript, "A packaged script invocation escapes the executable source root.");
                    if (!File.Exists(calledScript))
                        throw PowerShellCompilationSourceFailure.Create(
                            $"The called script '{call.RelativePath}' is missing. Supply the contained script or run the source in PowerShell.", current, call.Operand.Extent);
                    PowerShellCompilationPathSafety.EnsureNoLinks(sourceRoot, calledScript,
                        "A packaged script invocation target traverses a symbolic link or junction.");
                    if (discovered.Add(calledScript))
                        pending.Enqueue((calledScript, RuntimeSelected: false));
                    continue;
                }
                var expression = command.CommandElements.FirstOrDefault()
                    ?? throw new InvalidOperationException($"Dot-source expression at {current}:{command.Extent.StartLineNumber} has no path.");
                if (expression is ScriptBlockExpressionAst)
                    continue;
                if (IsAcceptedConventionalLoader(current, command, conventionalLoaders))
                    continue;
                string[] paths = Array.Empty<string>();
                var selectedAtImport = runtimeAlternatives != PowerShellModuleRuntimeAlternatives.Reject &&
                    PowerShellCompilationPathSafety.PathEquals(current, primarySource) &&
                    PowerShellCompilationSourcePathPlanner.TryPlanModuleAlternatives(ast, command, out _, out paths);
                if (selectedAtImport)
                {
                    if (runtimeAlternatives == PowerShellModuleRuntimeAlternatives.Skip)
                        continue;
                }
                else if (runtimeAlternatives == PowerShellModuleRuntimeAlternatives.StageAlternativesOnly &&
                         PowerShellCompilationPathSafety.PathEquals(current, primarySource))
                {
                    continue;
                }
                else if (PowerShellCompilationSourcePathPlanner.TryPlan(ast, command, allowDerivedPaths, out var fact))
                {
                    paths = new[] { fact!.RelativePath };
                }
                else
                {
                    throw PowerShellCompilationSourceFailure.Create(
                        $"Dot-source expression at {current}:{command.Extent.StartLineNumber} must be a literal $PSScriptRoot path or a supported guarded script-root assignment for portable staging.",
                        current, expression.Extent);
                }

                foreach (var path in paths)
                {
                    var relativePath = NormalizeRelativePath(path);
                    if (Path.IsPathRooted(relativePath) || WildcardPattern.ContainsWildcardCharacters(relativePath))
                        throw new InvalidOperationException($"Dot-source path '{relativePath}' at {current}:{command.Extent.StartLineNumber} must be a contained literal path without wildcards.");
                    var dependency = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!, relativePath));
                    PowerShellCompilationPathSafety.EnsureContained(sourceRoot, dependency, $"Dot-source path at {current}:{command.Extent.StartLineNumber} escapes the hybrid module source root.");
                    if (selectedAtImport && PowerShellCompilationPathSafety.PathEquals(dependency, primarySource))
                        throw new InvalidOperationException($"Module switch alternative '{relativePath}' selects the importing source itself.");
                    if (!File.Exists(dependency))
                        throw new FileNotFoundException($"Dot-sourced hybrid module dependency '{relativePath}' was not found for {current}:{command.Extent.StartLineNumber}.", dependency);
                    PowerShellCompilationPathSafety.EnsureNoLinks(sourceRoot, dependency, $"Dot-source path at {current}:{command.Extent.StartLineNumber} traverses a symbolic link or junction, which is not allowed for hybrid staging.");
                    if (discovered.Add(dependency))
                        pending.Enqueue((dependency, runtimeSelected || selectedAtImport));
                }
            }
        }
        return discovered.ToArray();
    }

    internal static bool IsStandaloneTypeResolutionError(ParseError error) => error.ErrorId == "TypeNotFound";

    private static bool IsAcceptedConventionalLoader(
        string sourcePath,
        CommandAst command,
        IReadOnlyCollection<PowerShellConventionalLoaderIdentity>? conventionalLoaders)
        => conventionalLoaders?.Any(loader =>
            loader.StartOffset == command.Extent.StartOffset &&
            PowerShellCompilationPathSafety.PathEquals(loader.SourcePath, sourcePath)) == true;

    private static string NormalizeRelativePath(string path)
        => path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

}

/// <summary>Controls discovery of guarded paths that the authored module selects at import.</summary>
internal enum PowerShellModuleRuntimeAlternatives
{
    Reject,
    Skip,
    Stage,
    StageAlternativesOnly
}

/// <summary>Describes a staged hybrid dependency and whether PowerForge generated its current contents.</summary>
internal sealed class PowerShellStagedModuleFile
{
    internal PowerShellStagedModuleFile(string path, bool isGenerated)
    {
        Path = path;
        IsGenerated = isGenerated;
    }

    internal string Path { get; }

    internal bool IsGenerated { get; }
}
