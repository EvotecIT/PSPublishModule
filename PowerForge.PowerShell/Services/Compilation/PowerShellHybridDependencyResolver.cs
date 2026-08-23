using System.Management.Automation;
using System.Management.Automation.Language;
using System.Runtime.InteropServices;
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

    internal static string[] CopyDependencies(
        string sourcePath,
        string moduleDirectory,
        IEnumerable<string>? additionalEntryPaths = null)
    {
        var sourceRoot = Path.GetFullPath(Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory());
        var comparer = GetPathComparer();
        var discovered = new HashSet<string>(comparer);
        var pending = new Queue<string>();
        var copied = new List<string>();
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
            foreach (var command in ast.FindAll(
                         static node => node is CommandAst { InvocationOperator: TokenKind.Dot },
                         searchNestedScriptBlocks: true).Cast<CommandAst>())
            {
                var expression = command.CommandElements.FirstOrDefault()
                    ?? throw new InvalidOperationException($"Dot-source expression at {current}:{command.Extent.StartLineNumber} has no path.");
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

                var target = Path.GetFullPath(Path.Combine(moduleDirectory, FrameworkCompatibility.GetRelativePath(sourceRoot, dependency)));
                PowerShellCompilationPathSafety.EnsureContained(Path.GetFullPath(moduleDirectory), target, $"Dot-source target for {current}:{command.Extent.StartLineNumber} escapes the hybrid module staging root.");
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? moduleDirectory);
                if (File.Exists(target))
                {
                    if (!File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(dependency)))
                        throw new InvalidOperationException($"Dot-sourced hybrid module dependency '{relativePath}' collides with a generated or manifest-staged artifact.");
                }
                else
                {
                    File.Copy(dependency, target, overwrite: false);
                    copied.Add(target);
                }
                pending.Enqueue(dependency);
            }
        }
        return copied.ToArray();
    }

    private static string ReadLiteralPath(CommandElementAst expression, string sourcePath, int line)
    {
        if (expression is ExpandableStringExpressionAst expandable &&
            expandable.NestedExpressions.Count == 1 &&
            expandable.NestedExpressions[0] is VariableExpressionAst variable &&
            variable.VariablePath.UserPath.Equals("PSScriptRoot", StringComparison.OrdinalIgnoreCase))
        {
            var match = ScriptRootPath.Match(expandable.Value);
            if (match.Success)
                return match.Groups["suffix"].Value.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        throw new InvalidOperationException($"Dot-source expression at {sourcePath}:{line} must be a literal $PSScriptRoot path for portable hybrid staging.");
    }

    private static string NormalizeRelativePath(string path)
        => path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

    private static StringComparer GetPathComparer()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
