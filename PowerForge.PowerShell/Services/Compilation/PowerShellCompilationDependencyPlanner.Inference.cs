using System.Management.Automation.Language;

namespace PowerForge;

public sealed partial class PowerShellCompilationDependencyPlanner
{
    private static IEnumerable<string> DiscoverLiteralResources(IEnumerable<string> sourceFiles, string moduleRoot)
    {
        foreach (var sourceFile in sourceFiles.Distinct(PowerShellCompilationPathSafety.PathComparer))
        {
            var ast = Parser.ParseFile(sourceFile, out _, out var errors);
            if (errors.Length > 0) continue;
            foreach (var literal in ast.FindAll(static node => node is ExpandableStringExpressionAst, searchNestedScriptBlocks: true)
                         .OfType<ExpandableStringExpressionAst>())
            {
                if (!IsHighConfidenceResourceContext(literal) || literal.NestedExpressions.Count != 1 ||
                    literal.NestedExpressions[0] is not VariableExpressionAst variable ||
                    !variable.VariablePath.UserPath.Equals("PSScriptRoot", StringComparison.OrdinalIgnoreCase))
                    continue;
                var text = literal.Extent.Text.Trim().Trim('"', '\'');
                var prefixes = new[] { "$PSScriptRoot/", "$PSScriptRoot\\", "${PSScriptRoot}/", "${PSScriptRoot}\\" };
                var prefix = prefixes.FirstOrDefault(candidate => text.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));
                if (prefix is null) continue;
                var relative = text.Substring(prefix.Length).Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                if (relative.Length == 0 || relative.IndexOfAny(new[] { '$', '`', '*', '?' }) >= 0) continue;
                var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourceFile)) ?? moduleRoot;
                var fullPath = Path.GetFullPath(Path.Combine(sourceDirectory, relative));
                PowerShellCompilationPathSafety.EnsureContained(moduleRoot, fullPath, $"Inferred resource literal '{literal.Extent.Text}' escapes the source root.");
                if (File.Exists(fullPath))
                    PowerShellCompilationPathSafety.EnsureNoLinks(moduleRoot, fullPath, $"Inferred resource literal '{literal.Extent.Text}' traverses a symbolic link or junction.");
                var extension = Path.GetExtension(fullPath);
                if (string.IsNullOrEmpty(extension))
                    continue;
                if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".psd1", StringComparison.OrdinalIgnoreCase))
                    continue;
                yield return fullPath;
            }
        }
    }

    private static bool IsHighConfidenceResourceContext(Ast node)
    {
        for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is ParamBlockAst or ParameterAst or AttributeAst)
                return false;
        }
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is CommandAst or InvokeMemberExpressionAst) return true;
            if (parent is StatementAst or ScriptBlockAst) return false;
        }
        return false;
    }
}
