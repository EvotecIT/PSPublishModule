using System.Management.Automation.Language;
using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class PowerShellCompilationDependencyGraphBuilder
{
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

        foreach (var module in (IEnumerable<Microsoft.PowerShell.Commands.ModuleSpecification>?)ast.ScriptRequirements?.RequiredModules ??
                     Array.Empty<Microsoft.PowerShell.Commands.ModuleSpecification>())
        {
            var nodeId = AddReference(
                module.Name,
                directory,
                PowerShellCompilationDependencyNodeKind.ExternalModule,
                HostedOrRejected(),
                "Static #requires module specification parsed by the PowerShell front end.",
                targetFramework,
                runtimeIdentifier);
            var identity = _nodes[nodeId].Identity;
            identity.Name = module.Name;
            identity.Version = module.RequiredVersion?.ToString() ?? module.Version?.ToString() ?? module.MaximumVersion ?? string.Empty;
            identity.MinimumVersion = module.Version?.ToString() ?? string.Empty;
            identity.RequiredVersion = module.RequiredVersion?.ToString() ?? string.Empty;
            identity.MaximumVersion = module.MaximumVersion ?? string.Empty;
            identity.Guid = module.Guid?.ToString("D") ?? string.Empty;
            identity.Provenance = "ScriptRequirementsModuleSpecification";
            AddEdge(sourceId, nodeId, PowerShellCompilationDependencyEdgeKind.RequiresModule, module.ToString());
        }

        foreach (var command in ast.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true)
                     .Cast<CommandAst>()
                     .OrderBy(static item => item.Extent.StartOffset))
        {
            DiscoverCommandEdge(command, sourceId, directory, targetFramework, runtimeIdentifier);
        }

        foreach (var invocation in ast.FindAll(static node => node is InvokeMemberExpressionAst, searchNestedScriptBlocks: true)
                     .Cast<InvokeMemberExpressionAst>()
                     .Where(static invocation => invocation.Static)
                     .OrderBy(static invocation => invocation.Extent.StartOffset))
        {
            DiscoverComActivation(invocation, sourceId, targetFramework);
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
            AddComActivation(progId!, isClsid: false, "PowerShell.NewObject.ComObject", command.Extent.Text, sourceId, targetFramework);
        }
    }

    private void DiscoverComActivation(
        InvokeMemberExpressionAst invocation,
        string sourceId,
        string? targetFramework)
    {
        if (invocation.Expression is not TypeExpressionAst type ||
            !type.TypeName.FullName.Equals("type", StringComparison.OrdinalIgnoreCase) ||
            invocation.Arguments.Count == 0)
            return;
        var member = invocation.Member.Extent.Text.Trim('\'', '"');
        var isProgId = member.Equals("GetTypeFromProgID", StringComparison.OrdinalIgnoreCase);
        var isClsid = member.Equals("GetTypeFromCLSID", StringComparison.OrdinalIgnoreCase);
        if ((!isProgId && !isClsid) || !TryGetExpressionLiteral(invocation.Arguments[0], out var value))
            return;
        AddComActivation(value!, isClsid, "System.Type." + member, invocation.Extent.Text, sourceId, targetFramework);
    }

    private void AddComActivation(
        string identityValue,
        bool isClsid,
        string adapter,
        string evidence,
        string sourceId,
        string? targetFramework)
    {
        var disposition = _mode == PowerShellCompilationMode.Strict
            ? PowerShellCompilationDependencyGraphDisposition.Rejected
            : PowerShellCompilationDependencyGraphDisposition.Hosted;
        var nodeId = AddExternalNode(
            identityValue,
            PowerShellCompilationDependencyNodeKind.ComObject,
            disposition,
            _mode == PowerShellCompilationMode.Strict
                ? "Strict compilation rejects COM activation until a typed COM adapter owns activation, apartment state, errors, and cleanup."
                : "COM activation is owned by the hosted Windows adapter; the invoking host supplies apartment state and cleanup.",
            string.Empty,
            targetFramework,
            "win");
        var identity = _nodes[nodeId].Identity;
        identity.Guid = isClsid ? identityValue : string.Empty;
        identity.InteropAdapter = adapter;
        identity.ApartmentState = "HostThread";
        identity.Provenance = isClsid ? "StaticComClsid" : "StaticComProgId";
        AddEdge(sourceId, nodeId, PowerShellCompilationDependencyEdgeKind.ComActivation, evidence);
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

    private static bool TryGetExpressionLiteral(ExpressionAst expression, out string? value)
    {
        if (expression is ConvertExpressionAst conversion)
            expression = conversion.Child;
        value = expression switch
        {
            StringConstantExpressionAst literal => literal.Value,
            ExpandableStringExpressionAst expandable when expandable.NestedExpressions.Count == 0 => expandable.Value,
            ConstantExpressionAst constant when constant.Value is Guid guid => guid.ToString("D"),
            _ => null
        };
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string TrimLiteral(string value)
        => value.Trim().Trim('\'', '"').Trim();
}
