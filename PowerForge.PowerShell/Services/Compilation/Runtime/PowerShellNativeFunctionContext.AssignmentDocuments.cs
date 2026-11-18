namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation.Language;
    using System.Runtime.CompilerServices;

    public sealed partial class PowerShellNativeFunctionContext
    {
        // Embedded source strings live with the generated assembly. Weak keys also
        // permit callers supplying transient documents to release the parsed tree.
        private static readonly ConditionalWeakTable<string, AssignmentDocument> AssignmentDocuments = new();

        private ScriptBlockAst ParseAssignmentDocument(string source, string file, out ParseError[] errors)
        {
            var parsed = AssignmentDocuments.GetValue(source, text => ReadAssignmentDocument(text, file));
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
        }
    }
}
