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
    internal static string RewriteDependencySourceLoads(string source, string sourcePath, string artifactSourceRoot,
        IReadOnlyCollection<string> embeddedScriptPaths, IReadOnlyCollection<string> retainedScriptPaths)
    {
        // This rewrite uses text offsets; source admission uses sourcePath separately.
        // The shared overload also supports the Windows PowerShell 5.1 reference contract.
        var ast = Parser.ParseInput(source, out _, out var errors);
        if (errors.Length != 0)
            throw new InvalidOperationException($"Packaged dependency '{sourcePath}' could not be parsed while guarding source loads.");
        var plans = PlanDotSources(ast, sourcePath, embeddedScriptPaths, allowDerivedPaths: true)
            .Concat(PlanScriptCalls(ast, sourcePath, retainedScriptPaths).Values);
        var replacements = AddPathOperandGuards(ast,
            AddSourceOperandGuards(plans, artifactSourceRoot, Array.Empty<SourceReplacement>()));
        var rewritten = new StringBuilder(source);
        foreach (var replacement in replacements)
        {
            rewritten.Remove(replacement.StartOffset, replacement.EndOffset - replacement.StartOffset);
            rewritten.Insert(replacement.StartOffset, replacement.Text);
        }
        return rewritten.ToString();
    }

    private static PackagedSourceOperand[] PlanDotSources(ScriptBlockAst ast, string sourcePath,
        IReadOnlyCollection<string> embeddedScriptPaths, bool allowDerivedPaths)
    {
        var allowed = IndexSourcePaths(embeddedScriptPaths);
        var directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        var plans = new List<PackagedSourceOperand>();
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
            if (!allowed.TryGetValue(targetPath, out var embeddedPath))
                throw new InvalidOperationException($"Packaged executable generation does not support dot-sourced command '{command.Extent.Text}' because resolved dependency '{targetPath}' is not embedded with the artifact.");
            plans.Add(new PackagedSourceOperand(fact, embeddedPath));
        }
        return plans.ToArray();
    }

    private static Dictionary<CommandAst, PackagedSourceOperand> PlanScriptCalls(ScriptBlockAst ast, string sourcePath,
        IReadOnlyCollection<string>? retainedScriptPaths)
    {
        var plans = new Dictionary<CommandAst, PackagedSourceOperand>();
        if (retainedScriptPaths is not { Count: > 0 }) return plans;
        var allowed = IndexSourcePaths(retainedScriptPaths);
        var directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        foreach (var command in ast.FindAll(static node => node is CommandAst { InvocationOperator: TokenKind.Ampersand },
                     searchNestedScriptBlocks: true).Cast<CommandAst>())
        {
            if (PowerShellCompilationSourcePathPlanner.PlanScriptCall(command) is not { } fact) continue;
            var relative = fact.RelativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (WildcardPattern.ContainsWildcardCharacters(relative)) continue;
            var target = Path.GetFullPath(Path.Combine(directory, relative));
            if (allowed.TryGetValue(target, out var retainedPath))
                plans.Add(command, new PackagedSourceOperand(fact, retainedPath, "ValidateCalledSource"));
        }
        return plans;
    }

    private static Dictionary<string, string> IndexSourcePaths(IEnumerable<string> paths)
    {
        var comparer = PowerShellCompilationPathSafety.PathComparer;
        return paths.Select(Path.GetFullPath).Distinct(comparer)
            .ToDictionary(static path => path, static path => path, comparer);
    }

    private static SourceReplacement[] AddSourceOperandGuards(IEnumerable<PackagedSourceOperand> plans, string artifactSourceRoot,
        SourceReplacement[] existing)
    {
        var guards = new List<SourceReplacement>();
        foreach (var plan in plans)
        {
            var extent = plan.Fact.Operand.Extent;
            PowerShellCompilationPathSafety.EnsureContained(artifactSourceRoot, plan.TargetPath,
                $"Packaged source target '{plan.TargetPath}' escapes its source root.");
            if (existing.Any(replacement => replacement.StartOffset < extent.EndOffset && replacement.EndOffset > extent.StartOffset &&
                    (replacement.StartOffset < extent.StartOffset || replacement.EndOffset > extent.EndOffset)))
                throw new InvalidOperationException("A packaged source-load guard overlaps another script-semantics rewrite.");
            var operand = RenderGuardOperand(plan.Fact.Operand, existing);
            var relative = FrameworkCompatibility.GetRelativePath(artifactSourceRoot, plan.TargetPath).Replace('\\', '/');
            guards.Add(new SourceReplacement(extent.StartOffset, extent.EndOffset,
                "([PowerForge.Compiled.PowerForgePackagedEntryPoint]::" + plan.GuardMethod + "((" + operand + "), '" +
                CodeGeneration.EscapeSingleQuotedStringContent(relative) + "'))"));
        }
        return existing.Where(replacement => !guards.Any(guard => replacement.StartOffset >= guard.StartOffset &&
                    replacement.EndOffset <= guard.EndOffset))
            .Concat(guards).OrderByDescending(static replacement => replacement.StartOffset).ToArray();
    }

    private sealed class PackagedSourceOperand
    {
        internal PackagedSourceOperand(PowerShellCompilationSourcePathFact fact, string targetPath, string guardMethod = "ValidateDotSource")
        {
            Fact = fact;
            TargetPath = targetPath;
            GuardMethod = guardMethod;
        }
        internal PowerShellCompilationSourcePathFact Fact { get; }
        internal string TargetPath { get; }
        internal string GuardMethod { get; }
    }
}
