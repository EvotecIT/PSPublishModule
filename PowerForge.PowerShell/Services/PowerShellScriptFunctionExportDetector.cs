using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Detects exported PowerShell script functions using the PowerShell AST parser.
/// </summary>
public sealed class PowerShellScriptFunctionExportDetector : IScriptFunctionExportDetector
{
    /// <inheritdoc />
    public IReadOnlyList<string> DetectScriptFunctions(IEnumerable<string> scriptFiles)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in scriptFiles ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                continue;

            try
            {
                Token[] tokens;
                ParseError[] errors;
                var ast = Parser.ParseFile(file, out tokens, out errors);
                if (errors is { Length: > 0 })
                    continue;

                var functions = ast.FindAll(node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
                    .Cast<FunctionDefinitionAst>();
                foreach (var function in functions)
                {
                    if (!string.IsNullOrWhiteSpace(function.Name))
                        result.Add(function.Name);
                }
            }
            catch
            {
                // best effort
            }
        }

        return result.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
