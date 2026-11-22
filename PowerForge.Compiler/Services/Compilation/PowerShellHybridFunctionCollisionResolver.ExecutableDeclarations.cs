using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

internal static partial class PowerShellHybridFunctionCollisionResolver
{
    /// <summary>Qualifies only the declaration files loaded by the guarded executable source owner.</summary>
    internal static HashSet<string> FindGuardedExecutableDeclarationPaths(IEnumerable<ParsedSourceDocument> documents,
        string? entryPath, PowerShellCompilationCapability capabilities)
    {
        var none = new HashSet<string>(PowerShellCompilationPathSafety.PathComparer);
        if (string.IsNullOrWhiteSpace(entryPath) || capabilities != PowerShellCompilationCapabilities.HybridExecutable)
            return none;
        try
        {
            var entry = Path.GetFullPath(entryPath);
            var root = Path.GetDirectoryName(entry)!;
            var sources = documents.ToDictionary(document => Path.GetFullPath(document.Path),
                PowerShellCompilationPathSafety.PathComparer);
            if (!sources.ContainsKey(entry) || sources.Count < 2) return none;
            foreach (var source in sources.Values)
            {
                if (source.Errors.Length != 0) return none;
                PowerShellCompilationPathSafety.EnsureContained(root, source.Path, "Executable declaration source escapes its entry root.");
                PowerShellCompilationPathSafety.EnsureNoLinks(root, source.Path, "Executable declaration source traverses a link.");
                if (PowerShellCompilationPathSafety.PathEquals(source.Path, entry)) continue;
                var syntax = source.SyntaxRoot;
                if (syntax.ParamBlock is not null || syntax.DynamicParamBlock is not null || syntax.BeginBlock is not null ||
                    syntax.ProcessBlock is not null || syntax.ScriptRequirements is not null || PowerShellSourceParser.HasUsingStatements(syntax) ||
                    syntax.GetType().GetProperty("CleanBlock")?.GetValue(syntax) is not null ||
                    syntax.EndBlock?.Unnamed == false || syntax.EndBlock?.Traps is { Count: > 0 } ||
                    syntax.EndBlock?.Statements.Any(static statement => !IsGuardedDeclarationStatement(statement)) == true)
                    return none;
            }
            var reached = new HashSet<string>(PowerShellCompilationPathSafety.PathComparer) { entry };
            var pending = new Queue<string>();
            pending.Enqueue(entry);
            while (pending.Count != 0)
            {
                var current = sources[pending.Dequeue()];
                foreach (var command in current.SyntaxRoot.FindAll(static node => node is CommandAst { InvocationOperator: TokenKind.Dot },
                             searchNestedScriptBlocks: true).Cast<CommandAst>())
                {
                    if (command.CommandElements.FirstOrDefault() is ScriptBlockExpressionAst) continue;
                    if (command.Parent is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
                        !ReferenceEquals(pipeline.Parent, current.SyntaxRoot.EndBlock) ||
                        !PowerShellCompilationSourcePathPlanner.TryPlan(current.SyntaxRoot, command, allowDerivedPaths: true, out var fact) ||
                        WildcardPattern.ContainsWildcardCharacters(fact!.RelativePath)) return none;
                    var relative = fact.RelativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                    if (Path.IsPathRooted(relative)) return none;
                    var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current.Path)!, relative));
                    if (!sources.ContainsKey(target) || PowerShellCompilationPathSafety.PathEquals(target, entry)) return none;
                    if (reached.Add(target)) pending.Enqueue(target);
                }
            }
            if (reached.Count != sources.Count) return none;
            reached.Remove(entry);
            return reached;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or
                                        NotSupportedException or InvalidOperationException)
        {
            return none;
        }
    }

    private static bool IsGuardedDeclarationStatement(StatementAst statement)
        => statement is FunctionDefinitionAst || statement is PipelineAst { PipelineElements.Count: 1 } pipeline &&
           pipeline.PipelineElements[0] is CommandAst { InvocationOperator: TokenKind.Dot };
}
