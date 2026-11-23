namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Management.Automation.Language;
    using System.Runtime.CompilerServices;

    public sealed partial class PowerShellNativeFunctionContext
    {
        // PowerShell type-name metadata belongs to an execution context. Never
        // share its parsed trees across runspaces, even for identical source.
        // Weak context and source keys allow both owners to release their trees.
        private static readonly ConditionalWeakTable<object, ConditionalWeakTable<string, AssignmentDocument>> AssignmentDocuments = new();
        private readonly ConditionalWeakTable<string, AssignmentDocument> _moduleDocuments = new();

        private string ModuleOperationFile(string authoredFile)
            => _isModuleInitializer && _contract.DefiningFile.GetValue(FunctionContext) is string { Length: > 0 } deployedFile
                ? deployedFile : authoredFile;

        private AssignmentDocument GetModuleDocument(string source, string file)
        {
            var document = _moduleDocuments.GetValue(source, text => ReadOwnedModuleDocument(text, file));
            return string.Equals(document.File, file, StringComparison.Ordinal) ? document : ReadOwnedModuleDocument(source, file);
        }

        private AssignmentDocument ReadOwnedModuleDocument(string source, string file)
        {
            var document = ReadAssignmentDocument(source, file);
            if (_moduleTypesOwner is not null)
                PowerShellNativeFunctionHost.AttachModuleTypeMetadata(document.Ast, _moduleTypesOwner);
            return document;
        }

        private ScriptBlockAst ParseAssignmentDocument(string source, string file, out ParseError[] errors)
        {
            if (_isModuleInitializer || _moduleTypesOwner is not null)
            {
                var module = GetModuleDocument(source, ModuleOperationFile(file));
                errors = module.Errors;
                return module.Ast;
            }
            if (_functionTypes is { Classes.Length: > 0 })
            {
                // A detached target may contain lexical class references tied to this parse's declarations.
                // Its native Type metadata belongs only to the active compiled function instance.
                var owned = ReadAssignmentDocument(source, file);
                errors = owned.Errors;
                return owned.Ast;
            }
            var documents = AssignmentDocuments.GetValue(_executionContext,
                static _ => new ConditionalWeakTable<string, AssignmentDocument>());
            var parsed = documents.GetValue(source, text => ReadAssignmentDocument(text, file));
            // The same source text can legitimately belong to another file. Keep
            // its extents independent instead of returning the first file's AST.
            if (!string.Equals(parsed.File, file, StringComparison.Ordinal))
                parsed = ReadAssignmentDocument(source, file);
            errors = parsed.Errors;
            return parsed.Ast;
        }

        private AssignmentDocument ReadAssignmentDocument(string source, string file)
        {
            var arguments = new object[] { source, file, null!, null! };
            var ast = (ScriptBlockAst)PowerShellNativeFunctionHost.Invoke(_contract.ParseInputWithFile, null, arguments)!;
            return new AssignmentDocument(file, ast, (ParseError[])arguments[3]);
        }

        private sealed class AssignmentDocument
        {
            internal AssignmentDocument(string file, ScriptBlockAst ast, ParseError[] errors)
            {
                File = file;
                Ast = ast;
                Errors = errors;
            }

            internal string File { get; }
            internal ScriptBlockAst Ast { get; }
            internal ParseError[] Errors { get; }
            private Dictionary<(int Start, int End), FunctionDefinitionAst>? _declarations;

            internal FunctionDefinitionAst? FindDeclaration(int start, int end)
            {
                _declarations ??= Ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: true)
                    .OfType<FunctionDefinitionAst>().ToDictionary(static definition =>
                        (definition.Extent.StartOffset, definition.Extent.EndOffset));
                return _declarations.TryGetValue((start, end), out var definition) ? definition : null;
            }
        }
    }
}
