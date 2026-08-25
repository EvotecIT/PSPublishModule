using System.Management.Automation;
using System.Management.Automation.Language;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Discovers and stages contained literal dot-sourced files required by retained hybrid module source.
/// </summary>
internal static class PowerShellHybridDependencyResolver
{
    private static readonly Regex ScriptRootPath = new(
        @"^\$(?:\{)?PSScriptRoot(?:\})?(?<suffix>[\\/].+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static PowerShellHybridDependencyCopy[] CopyDependencies(
        string sourcePath,
        string moduleDirectory,
        IEnumerable<string>? additionalEntryPaths = null,
        Func<string, string?>? contentTransformer = null,
        IEnumerable<string>? explicitDependencyPaths = null,
        IReadOnlyCollection<PowerShellConventionalLoaderIdentity>? conventionalLoaders = null)
    {
        var sourceRoot = Path.GetFullPath(Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory());
        var comparer = PowerShellCompilationPathSafety.PathComparer;
        var entryPaths = new HashSet<string>(
            new[] { sourcePath }.Concat(additionalEntryPaths ?? Array.Empty<string>()).Select(Path.GetFullPath),
            comparer);
        var discoveryEntries = (additionalEntryPaths ?? Array.Empty<string>())
            .Concat(explicitDependencyPaths ?? Array.Empty<string>());
        var copied = new List<PowerShellHybridDependencyCopy>();
        foreach (var dependency in DiscoverDependencies(sourcePath, discoveryEntries, conventionalLoaders))
        {
            if (entryPaths.Contains(dependency))
                continue;
            var target = Path.GetFullPath(Path.Combine(moduleDirectory, FrameworkCompatibility.GetRelativePath(sourceRoot, dependency)));
            PowerShellCompilationPathSafety.EnsureContained(Path.GetFullPath(moduleDirectory), target, $"Dot-source target for '{dependency}' escapes the hybrid module staging root.");
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? moduleDirectory);
            var transformedContent = contentTransformer?.Invoke(dependency);
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
                File.WriteAllText(target, transformedContent, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                copied.Add(new PowerShellHybridDependencyCopy(target, isGenerated: true));
            }
            else
            {
                File.Copy(dependency, target, overwrite: false);
                copied.Add(new PowerShellHybridDependencyCopy(target, isGenerated: false));
            }
        }
        return copied.ToArray();
    }

    internal static string[] DiscoverDependencies(
        string sourcePath,
        IEnumerable<string>? additionalEntryPaths = null,
        IReadOnlyCollection<PowerShellConventionalLoaderIdentity>? conventionalLoaders = null)
        => DiscoverDependenciesCore(sourcePath, additionalEntryPaths, moduleScopeOnly: false, conventionalLoaders);

    internal static string[] DiscoverModuleScopeDependencies(string sourcePath)
        => DiscoverDependenciesCore(sourcePath, additionalEntryPaths: null, moduleScopeOnly: true, conventionalLoaders: null);

    private static string[] DiscoverDependenciesCore(
        string sourcePath,
        IEnumerable<string>? additionalEntryPaths,
        bool moduleScopeOnly,
        IReadOnlyCollection<PowerShellConventionalLoaderIdentity>? conventionalLoaders)
    {
        var sourceRoot = Path.GetFullPath(Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory());
        var discovered = new HashSet<string>(PowerShellCompilationPathSafety.PathComparer);
        var pending = new Queue<string>();
        foreach (var entryPath in new[] { sourcePath }.Concat(additionalEntryPaths ?? Array.Empty<string>()))
        {
            var entry = Path.GetFullPath(entryPath);
            PowerShellCompilationPathSafety.EnsureContained(sourceRoot, entry, $"Hybrid module runtime source '{entry}' escapes the hybrid module source root.");
            if (!File.Exists(entry))
                throw new FileNotFoundException($"Hybrid module runtime source '{entry}' was not found.", entry);
            PowerShellCompilationPathSafety.EnsureNoLinks(sourceRoot, entry, $"Hybrid module runtime source '{entry}' traverses a symbolic link or junction, which is not allowed for hybrid staging.");
            if (discovered.Add(entry))
                pending.Enqueue(entry);
        }

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            Token[] tokens;
            ParseError[] errors;
            var ast = Parser.ParseFile(current, out tokens, out errors);
            if (errors.Length > 0)
                throw new InvalidOperationException($"Dot-sourced hybrid module dependency '{current}' could not be parsed.");
            var dotSourceCommands = moduleScopeOnly
                ? (ast.EndBlock is null ? Enumerable.Empty<StatementAst>() : ast.EndBlock.Statements)
                    .OfType<PipelineAst>()
                    .Where(static pipeline => pipeline.PipelineElements.Count == 1)
                    .Select(static pipeline => pipeline.PipelineElements[0])
                    .OfType<CommandAst>()
                    .Where(static command => command.InvocationOperator == TokenKind.Dot)
                : ast.FindAll(
                        static node => node is CommandAst { InvocationOperator: TokenKind.Dot },
                        searchNestedScriptBlocks: true)
                    .Cast<CommandAst>();
            foreach (var command in dotSourceCommands)
            {
                var expression = command.CommandElements.FirstOrDefault()
                    ?? throw new InvalidOperationException($"Dot-source expression at {current}:{command.Extent.StartLineNumber} has no path.");
                if (IsAcceptedConventionalLoader(current, command, conventionalLoaders))
                    continue;
                var relativePath = NormalizeRelativePath(ReadLiteralPath(expression, current, command.Extent.StartLineNumber));
                if (Path.IsPathRooted(relativePath) || WildcardPattern.ContainsWildcardCharacters(relativePath))
                    throw new InvalidOperationException($"Dot-source path '{relativePath}' at {current}:{command.Extent.StartLineNumber} must be a contained literal path without wildcards.");
                var dependency = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!, relativePath));
                PowerShellCompilationPathSafety.EnsureContained(sourceRoot, dependency, $"Dot-source path at {current}:{command.Extent.StartLineNumber} escapes the hybrid module source root.");
                if (!File.Exists(dependency))
                    throw new FileNotFoundException($"Dot-sourced hybrid module dependency '{relativePath}' was not found for {current}:{command.Extent.StartLineNumber}.", dependency);
                PowerShellCompilationPathSafety.EnsureNoLinks(sourceRoot, dependency, $"Dot-source path at {current}:{command.Extent.StartLineNumber} traverses a symbolic link or junction, which is not allowed for hybrid staging.");
                if (!discovered.Add(dependency))
                    continue;
                pending.Enqueue(dependency);
            }
        }
        return discovered.ToArray();
    }

    private static bool IsAcceptedConventionalLoader(
        string sourcePath,
        CommandAst command,
        IReadOnlyCollection<PowerShellConventionalLoaderIdentity>? conventionalLoaders)
        => conventionalLoaders?.Any(loader =>
            loader.StartOffset == command.Extent.StartOffset &&
            PowerShellCompilationPathSafety.PathEquals(loader.SourcePath, sourcePath)) == true;

    private static string ReadLiteralPath(CommandElementAst expression, string sourcePath, int line)
    {
        if (expression is ExpandableStringExpressionAst expandable &&
            expandable.NestedExpressions.Count == 1 &&
            expandable.NestedExpressions[0] is VariableExpressionAst variable &&
            variable.VariablePath.UserPath.Equals("PSScriptRoot", StringComparison.OrdinalIgnoreCase))
        {
            var match = ScriptRootPath.Match(expandable.Value);
            if (match.Success)
                return NormalizeRelativePath(match.Groups["suffix"].Value)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '\\', '/');
        }
        throw new InvalidOperationException($"Dot-source expression at {sourcePath}:{line} must be a literal $PSScriptRoot path for portable hybrid staging.");
    }

    private static string NormalizeRelativePath(string path)
        => path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

}

/// <summary>Describes a staged hybrid dependency and whether PowerForge generated its current contents.</summary>
internal sealed class PowerShellHybridDependencyCopy
{
    internal PowerShellHybridDependencyCopy(string path, bool isGenerated)
    {
        Path = path;
        IsGenerated = isGenerated;
    }

    internal string Path { get; }

    internal bool IsGenerated { get; }
}
