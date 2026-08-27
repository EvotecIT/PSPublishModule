using System.Management.Automation.Language;
using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class PowerShellCompilationDependencyGraphBuilder
{
    private static readonly Regex RequiresModulePattern = new(
        @"(?im)^\s*#requires\s+(?:-(?:modules?|pssnapin)\s+)(?<value>[^\r\n]+)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex DllImportPattern = new(
        @"(?i)\[(?:System\.Runtime\.InteropServices\.)?DllImport\s*\(\s*['""](?<value>[^'""]+)['""]",
        RegexOptions.CultureInvariant);

    private void DiscoverSourceEdges(
        string sourcePath,
        string sourceId,
        string? targetFramework,
        string? runtimeIdentifier)
    {
        if (!File.Exists(sourcePath)) return;
        var text = File.ReadAllText(sourcePath);
        var ast = Parser.ParseFile(sourcePath, out _, out var errors);
        if (errors.Length > 0) return;
        var directory = Path.GetDirectoryName(sourcePath) ?? _moduleRoot;

        foreach (var usingStatement in ast.FindAll(static node => node is UsingStatementAst, searchNestedScriptBlocks: false)
                     .Cast<UsingStatementAst>()
                     .OrderBy(static item => item.Extent.StartOffset))
        {
            var match = Regex.Match(
                usingStatement.Extent.Text,
                @"(?is)^\s*using\s+(?<kind>module|assembly)\s+(?<value>.+?)\s*$",
                RegexOptions.CultureInvariant);
            if (!match.Success) continue;
            var value = TrimLiteral(match.Groups["value"].Value);
            if (value.Length == 0) continue;
            var isAssembly = match.Groups["kind"].Value.Equals("assembly", StringComparison.OrdinalIgnoreCase);
            var nodeId = AddReference(
                value,
                directory,
                isAssembly ? PowerShellCompilationDependencyNodeKind.ManagedLibrary : PowerShellCompilationDependencyNodeKind.ExternalModule,
                isAssembly ? PowerShellCompilationDependencyGraphDisposition.Referenced : HostedOrRejected(),
                isAssembly ? "Static using assembly declaration." : "Static using module declaration.",
                targetFramework,
                runtimeIdentifier);
            AddEdge(
                sourceId,
                nodeId,
                isAssembly ? PowerShellCompilationDependencyEdgeKind.UsingAssembly : PowerShellCompilationDependencyEdgeKind.UsingModule,
                usingStatement.Extent.Text.Trim());
        }

        foreach (Match match in RequiresModulePattern.Matches(text))
        {
            foreach (var module in match.Groups["value"].Value.Split(',').Select(TrimLiteral).Where(static item => item.Length > 0))
            {
                var nodeId = AddExternalNode(
                    module,
                    PowerShellCompilationDependencyNodeKind.ExternalModule,
                    HostedOrRejected(),
                    "Static #requires module declaration.",
                    string.Empty,
                    targetFramework,
                    runtimeIdentifier);
                AddEdge(sourceId, nodeId, PowerShellCompilationDependencyEdgeKind.RequiresModule, match.Value.Trim());
            }
        }

        foreach (var command in ast.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true)
                     .Cast<CommandAst>()
                     .OrderBy(static item => item.Extent.StartOffset))
        {
            DiscoverCommandEdge(command, sourceId, directory, targetFramework, runtimeIdentifier);
        }

        foreach (Match match in DllImportPattern.Matches(text))
        {
            var library = match.Groups["value"].Value.Trim();
            var nodeId = AddReference(
                library,
                directory,
                PowerShellCompilationDependencyNodeKind.NativeLibrary,
                _mode == PowerShellCompilationMode.Strict
                    ? PowerShellCompilationDependencyGraphDisposition.External
                    : PowerShellCompilationDependencyGraphDisposition.Hosted,
                "Static DllImport native-library contract; RID, cancellation, and cleanup remain explicit deployment requirements.",
                targetFramework,
                runtimeIdentifier);
            AddEdge(sourceId, nodeId, PowerShellCompilationDependencyEdgeKind.NativeLoad, match.Value);
        }
    }

    private void DiscoverCommandEdge(
        CommandAst command,
        string sourceId,
        string directory,
        string? targetFramework,
        string? runtimeIdentifier)
    {
        var commandName = command.GetCommandName();
        if (string.IsNullOrWhiteSpace(commandName))
        {
            if (command.InvocationOperator == TokenKind.Dot && TryGetLiteral(command.CommandElements.Skip(1).FirstOrDefault(), out var dotSource))
            {
                var nodeId = AddReference(
                    dotSource!,
                    directory,
                    PowerShellCompilationDependencyNodeKind.Script,
                    _mode == PowerShellCompilationMode.Package
                        ? PowerShellCompilationDependencyGraphDisposition.Hosted
                        : PowerShellCompilationDependencyGraphDisposition.Compiled,
                    "Literal dot-source dependency.",
                    targetFramework,
                    runtimeIdentifier);
                AddEdge(sourceId, nodeId, PowerShellCompilationDependencyEdgeKind.DotSource, command.Extent.Text);
            }
            else if (command.InvocationOperator == TokenKind.Ampersand && TryGetLiteral(command.CommandElements.Skip(1).FirstOrDefault(), out var invoked))
                AddProcessEdge(invoked!, command, sourceId, directory, targetFramework, runtimeIdentifier);
            return;
        }

        if (commandName.Equals("Import-Module", StringComparison.OrdinalIgnoreCase) &&
            TryGetFirstPositionalLiteral(command, out var module))
        {
            var nodeId = AddReference(
                module!,
                directory,
                PowerShellCompilationDependencyNodeKind.ExternalModule,
                HostedOrRejected(),
                "Literal Import-Module contract; analysis does not import the module.",
                targetFramework,
                runtimeIdentifier);
            AddEdge(sourceId, nodeId, PowerShellCompilationDependencyEdgeKind.ImportModule, command.Extent.Text);
            return;
        }

        if (commandName.Equals("Add-Type", StringComparison.OrdinalIgnoreCase) &&
            TryGetNamedLiteral(command, new[] { "Path", "LiteralPath", "ReferencedAssemblies" }, out var assembly))
        {
            var nodeId = AddReference(
                assembly!,
                directory,
                PowerShellCompilationDependencyNodeKind.ManagedLibrary,
                PowerShellCompilationDependencyGraphDisposition.Referenced,
                "Literal Add-Type assembly reference; metadata is read without loading the assembly.",
                targetFramework,
                runtimeIdentifier);
            AddEdge(sourceId, nodeId, PowerShellCompilationDependencyEdgeKind.ManagedReference, command.Extent.Text);
            return;
        }

        if (commandName.Equals("Start-Process", StringComparison.OrdinalIgnoreCase) &&
            (TryGetNamedLiteral(command, new[] { "FilePath" }, out var process) || TryGetFirstPositionalLiteral(command, out process)))
        {
            AddProcessEdge(process!, command, sourceId, directory, targetFramework, runtimeIdentifier);
            return;
        }

        if (commandName.Equals("New-Object", StringComparison.OrdinalIgnoreCase) &&
            TryGetNamedLiteral(command, new[] { "ComObject" }, out var progId))
        {
            var disposition = _mode == PowerShellCompilationMode.Strict
                ? PowerShellCompilationDependencyGraphDisposition.Rejected
                : PowerShellCompilationDependencyGraphDisposition.Hosted;
            var nodeId = AddExternalNode(
                progId!,
                PowerShellCompilationDependencyNodeKind.ComObject,
                disposition,
                _mode == PowerShellCompilationMode.Strict
                    ? "Strict compilation rejects COM activation until a typed COM adapter exists."
                    : "COM activation remains hosted by Windows PowerShell semantics.",
                string.Empty,
                targetFramework,
                "win");
            AddEdge(sourceId, nodeId, PowerShellCompilationDependencyEdgeKind.RuntimeAsset, command.Extent.Text);
        }
    }

    private void AddProcessEdge(
        string process,
        CommandAst command,
        string sourceId,
        string directory,
        string? targetFramework,
        string? runtimeIdentifier)
    {
        var nodeId = AddReference(
            process,
            directory,
            PowerShellCompilationDependencyNodeKind.ExternalProcess,
            _mode == PowerShellCompilationMode.Strict
                ? PowerShellCompilationDependencyGraphDisposition.Rejected
                : PowerShellCompilationDependencyGraphDisposition.External,
            "External process requires explicit RID availability, exit/error mapping, cancellation, and cleanup handling.",
            targetFramework,
            runtimeIdentifier);
        AddEdge(sourceId, nodeId, PowerShellCompilationDependencyEdgeKind.ProcessTarget, command.Extent.Text);
    }

    private string AddReference(
        string value,
        string directory,
        PowerShellCompilationDependencyNodeKind kind,
        PowerShellCompilationDependencyGraphDisposition disposition,
        string note,
        string? targetFramework,
        string? runtimeIdentifier)
    {
        var normalized = value.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var path = Path.IsPathRooted(normalized) ? normalized : Path.Combine(directory, normalized);
        if (File.Exists(path))
        {
            return AddLocalNode(
                path,
                kind == PowerShellCompilationDependencyNodeKind.ExternalModule ? ClassifyLocalPath(path) : kind,
                PowerShellCompilationDependencyGraphRole.Dependency | PowerShellCompilationDependencyGraphRole.Deployment,
                disposition,
                note,
                targetFramework,
                runtimeIdentifier);
        }
        return AddExternalNode(value, kind, disposition, note, string.Empty, targetFramework, runtimeIdentifier);
    }

    private PowerShellCompilationDependencyGraphDisposition HostedOrRejected()
        => _mode == PowerShellCompilationMode.Strict
            ? PowerShellCompilationDependencyGraphDisposition.Rejected
            : PowerShellCompilationDependencyGraphDisposition.Hosted;

    private static bool TryGetFirstPositionalLiteral(CommandAst command, out string? value)
    {
        value = null;
        for (var index = 1; index < command.CommandElements.Count; index++)
        {
            if (command.CommandElements[index] is CommandParameterAst)
            {
                index++;
                continue;
            }
            return TryGetLiteral(command.CommandElements[index], out value);
        }
        return false;
    }

    private static bool TryGetNamedLiteral(CommandAst command, IReadOnlyCollection<string> names, out string? value)
    {
        value = null;
        for (var index = 1; index < command.CommandElements.Count - 1; index++)
        {
            if (command.CommandElements[index] is not CommandParameterAst parameter ||
                !names.Contains(parameter.ParameterName, StringComparer.OrdinalIgnoreCase))
                continue;
            return TryGetLiteral(command.CommandElements[index + 1], out value);
        }
        return false;
    }

    private static bool TryGetLiteral(CommandElementAst? element, out string? value)
    {
        value = element switch
        {
            StringConstantExpressionAst literal => literal.Value,
            ExpandableStringExpressionAst expandable when expandable.NestedExpressions.Count == 0 => expandable.Value,
            _ => null
        };
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string TrimLiteral(string value)
        => value.Trim().Trim('\'', '"').Trim();
}
