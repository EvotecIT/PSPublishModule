using System.Management.Automation;
using System.Management.Automation.Language;
using System.Text;

namespace PowerForge;

internal static partial class PowerShellPackagedScriptRewriter
{
    internal static void ValidateDotSources(ScriptBlockAst ast, string sourcePath,
        IReadOnlyCollection<string> embeddedScriptPaths, bool allowDerivedPaths = false)
        => PlanDotSources(ast, sourcePath, embeddedScriptPaths, allowDerivedPaths);

    /// <summary>Adds only source-load checks to retained dependency text; file-backed metadata and scope remain intact.</summary>
    internal static string RewriteDependencyDotSources(string source, string sourcePath, string artifactSourceRoot,
        IReadOnlyCollection<string> embeddedScriptPaths)
    {
        // This rewrite uses text offsets; source admission uses sourcePath separately.
        // The shared overload also supports the Windows PowerShell 5.1 reference contract.
        var ast = Parser.ParseInput(source, out _, out var errors);
        if (errors.Length != 0)
            throw new InvalidOperationException($"Packaged dependency '{sourcePath}' could not be parsed while guarding source loads.");
        var plans = PlanDotSources(ast, sourcePath, embeddedScriptPaths, allowDerivedPaths: true);
        var replacements = AddPathOperandGuards(ast,
            AddDotSourceGuards(plans, artifactSourceRoot, Array.Empty<SourceReplacement>()));
        var rewritten = new StringBuilder(source);
        foreach (var replacement in replacements)
        {
            rewritten.Remove(replacement.StartOffset, replacement.EndOffset - replacement.StartOffset);
            rewritten.Insert(replacement.StartOffset, replacement.Text);
        }
        return rewritten.ToString();
    }

    private static PackagedDotSource[] PlanDotSources(ScriptBlockAst ast, string sourcePath,
        IReadOnlyCollection<string> embeddedScriptPaths, bool allowDerivedPaths)
    {
        var allowed = embeddedScriptPaths.Select(Path.GetFullPath).ToHashSet(PowerShellCompilationPathSafety.PathComparer);
        var directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        var plans = new List<PackagedDotSource>();
        foreach (var command in ast.FindAll(static node => node is CommandAst { InvocationOperator: TokenKind.Dot },
                     searchNestedScriptBlocks: true).Cast<CommandAst>())
        {
            if (command.CommandElements.FirstOrDefault() is ScriptBlockExpressionAst) continue;
            if (!PowerShellCompilationSourcePathPlanner.TryPlan(ast, command, allowDerivedPaths, out var fact))
                throw new InvalidOperationException($"Packaged executable generation does not support dot-sourced command '{command.Extent.Text}' because its target is not a literal $PSScriptRoot path or a supported guarded script-root assignment embedded with the artifact.");
            var relativePath = fact!.RelativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(relativePath) || WildcardPattern.ContainsWildcardCharacters(relativePath))
                throw new InvalidOperationException($"Packaged dot-source path '{fact.RelativePath}' must be a contained path without wildcards.");
            string targetPath;
            try { targetPath = Path.GetFullPath(Path.Combine(directory, relativePath)); }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new InvalidOperationException($"Packaged executable generation cannot resolve dot-sourced command '{command.Extent.Text}' to an embedded dependency.", exception);
            }
            if (!allowed.Contains(targetPath))
                throw new InvalidOperationException($"Packaged executable generation does not support dot-sourced command '{command.Extent.Text}' because resolved dependency '{targetPath}' is not embedded with the artifact.");
            plans.Add(new PackagedDotSource(fact, targetPath));
        }
        return plans.ToArray();
    }

    private static SourceReplacement[] AddDotSourceGuards(PackagedDotSource[] plans, string artifactSourceRoot,
        SourceReplacement[] existing)
    {
        var guards = new List<SourceReplacement>();
        foreach (var plan in plans)
        {
            var extent = plan.Fact.Operand.Extent;
            PowerShellCompilationPathSafety.EnsureContained(artifactSourceRoot, plan.TargetPath,
                $"Packaged dot-source target '{plan.TargetPath}' escapes its source root.");
            var operand = new StringBuilder(extent.Text);
            foreach (var replacement in existing.Where(replacement => replacement.StartOffset >= extent.StartOffset &&
                         replacement.EndOffset <= extent.EndOffset).OrderByDescending(static replacement => replacement.StartOffset))
            {
                operand.Remove(replacement.StartOffset - extent.StartOffset, replacement.EndOffset - replacement.StartOffset);
                operand.Insert(replacement.StartOffset - extent.StartOffset, replacement.Text);
            }
            if (existing.Any(replacement => replacement.StartOffset < extent.EndOffset && replacement.EndOffset > extent.StartOffset &&
                    (replacement.StartOffset < extent.StartOffset || replacement.EndOffset > extent.EndOffset)))
                throw new InvalidOperationException("A packaged source-load guard overlaps another script-semantics rewrite.");
            var relative = FrameworkCompatibility.GetRelativePath(artifactSourceRoot, plan.TargetPath).Replace('\\', '/');
            guards.Add(new SourceReplacement(extent.StartOffset, extent.EndOffset,
                "([PowerForge.Compiled.PowerForgePackagedEntryPoint]::ValidateDotSource((" + operand + "), '" +
                relative.Replace("'", "''") + "'))"));
        }
        return existing.Where(replacement => !guards.Any(guard => replacement.StartOffset >= guard.StartOffset &&
                    replacement.EndOffset <= guard.EndOffset))
            .Concat(guards).OrderByDescending(static replacement => replacement.StartOffset).ToArray();
    }

    private sealed class PackagedDotSource
    {
        internal PackagedDotSource(PowerShellCompilationSourcePathFact fact, string targetPath)
        {
            Fact = fact;
            TargetPath = targetPath;
        }
        internal PowerShellCompilationSourcePathFact Fact { get; }
        internal string TargetPath { get; }
    }
}
