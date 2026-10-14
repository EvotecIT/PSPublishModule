namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Management.Automation;
    using System.Management.Automation.Language;
    using System.Reflection;
    using System.Runtime.ExceptionServices;

    /// <summary>Invokes qualified retained bytes with immutable authored positions and ordinary file dispatch.</summary>
    internal static class PowerShellPackagedScriptEntry
    {
        internal static PowerShellNativeFunctionHost.NativeScriptEntryLookup AddCommand(PowerShell shell,
            string path, string source, string authoredPath, string document, int[] edits)
        {
            var fullPath = System.IO.Path.GetFullPath(path);
            var info = shell.Runspace.SessionStateProxy.InvokeCommand.GetCommand(fullPath, CommandTypes.ExternalScript) as ExternalScriptInfo
                ?? throw new InvalidOperationException("The retained script entry could not be discovered as an external script.");
            var comparison = Environment.OSVersion.Platform == PlatformID.Win32NT ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(info.Path, fullPath, comparison) || !string.Equals(info.ScriptContents, source, StringComparison.Ordinal))
                throw new InvalidOperationException("The retained script entry differs from its qualified packaged source.");
            var flags = BindingFlags.Public | BindingFlags.NonPublic;
            var parser = typeof(Parser).GetMethod("ParseInput", flags | BindingFlags.Static, null,
                new[] { typeof(string), typeof(string), typeof(Token[]).MakeByRefType(), typeof(ParseError[]).MakeByRefType() }, null)
                ?? throw new NotSupportedException("PowerShell's authored-source parser is unavailable for retained entry positions.");
            var arguments = new object?[] { source, authoredPath, null, null };
            ScriptBlockAst ast;
            try { ast = (ScriptBlockAst)parser.Invoke(null, arguments)!; }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
            if (((ParseError[])arguments[3]!).Length != 0) throw new ParseException((ParseError[])arguments[3]!);
            var extentField = typeof(Ast).GetField("<Extent>k__BackingField", flags | BindingFlags.Instance);
            if (extentField is null || extentField.FieldType != typeof(IScriptExtent))
                throw new NotSupportedException("PowerShell's fresh-AST extent contract is unavailable for retained entry positions.");
            if (edits.Length % 3 != 0) throw new ArgumentException("Retained source edits require offset/old-length/new-length triples.", nameof(edits));
            var length = document.Length;
            for (var index = 0; index < edits.Length; index += 3)
            {
                if (edits[index] < 0 || edits[index + 1] < 0 || edits[index + 2] < 0 || edits[index] > length - edits[index + 1])
                    throw new ArgumentException("Retained source edits escape their preceding document.", nameof(edits));
                length += edits[index + 2] - edits[index + 1];
            }
            if (length != source.Length) throw new ArgumentException("Retained source edits do not describe the qualified payload.", nameof(edits));
            var lineStarts = new List<int> { 0 };
            for (var index = 0; index < document.Length; index++)
                if (document[index] == '\r' || document[index] == '\n')
                {
                    if (document[index] == '\r' && index + 1 < document.Length && document[index + 1] == '\n') index++;
                    lineStarts.Add(index + 1);
                }
            var starts = lineStarts.ToArray();
            // Parse only the qualified transformed bytes. Never compile authored metadata,
            // patch ScriptBlock.Create's shared cache, or change executable AST properties.
            foreach (var node in ast.FindAll(_ => true, searchNestedScriptBlocks: true))
            {
                var start = Map(node.Extent.StartOffset, false, edits);
                var end = Map(node.Extent.EndOffset, true, edits);
                // ScriptBlock.File owns automatic path variables. Retained AddScript used
                // unnamed scopes; keep that identity while executable positions name the author.
                extentField.SetValue(node, new AuthoredExtent(node is ScriptBlockAst ? string.Empty : authoredPath,
                    document, starts, start, end, node.Extent.Text));
            }
            var property = typeof(ExternalScriptInfo).GetProperty("ScriptBlock", flags | BindingFlags.Instance)
                ?? throw new NotSupportedException("PowerShell's external script owner is unavailable for retained entry positions.");
            property.SetValue(info, ast.GetScriptBlock(), null);
            // The native setter transfers file language mode. The scoped lookup retains
            // normal authorization, basic binding, caller frame and prior lookup hooks.
            var lookup = new PowerShellNativeFunctionHost.NativeScriptEntryLookup(shell.Runspace.SessionStateProxy.InvokeCommand, info);
            try { shell.AddCommand(info.Path, useLocalScope: false); return lookup; }
            catch { lookup.Dispose(); throw; }
        }

        private static int Map(int offset, bool end, int[] edits)
        {
            for (var index = edits.Length - 3; index >= 0; index -= 3)
            {
                var start = edits[index];
                var oldLength = edits[index + 1];
                var newLength = edits[index + 2];
                if (offset >= start + newLength) offset += oldLength - newLength;
                else if (offset > start) offset = start + (end ? oldLength : 0);
            }
            return offset;
        }

        private sealed class AuthoredExtent : IScriptExtent
        {
            private readonly string _text;
            internal AuthoredExtent(string path, string document, int[] lineStarts, int start, int end, string text)
            {
                if (end < start) throw new ArgumentException("An authored extent cannot end before its start.");
                // Extent.Text is executable identity (including exact declaration stubs),
                // not diagnostic text. Only positions and their document are projected.
                _text = text;
                StartScriptPosition = new AuthoredPosition(path, document, lineStarts, start);
                EndScriptPosition = new AuthoredPosition(path, document, lineStarts, end);
            }
            public string File => StartScriptPosition.File!;
            public IScriptPosition StartScriptPosition { get; }
            public IScriptPosition EndScriptPosition { get; }
            public int StartLineNumber => StartScriptPosition.LineNumber;
            public int StartColumnNumber => StartScriptPosition.ColumnNumber;
            public int EndLineNumber => EndScriptPosition.LineNumber;
            public int EndColumnNumber => EndScriptPosition.ColumnNumber;
            public int StartOffset => StartScriptPosition.Offset;
            public int EndOffset => EndScriptPosition.Offset;
            public string Text => _text;
        }

        private sealed class AuthoredPosition : IScriptPosition
        {
            private readonly string _document;
            internal AuthoredPosition(string path, string document, int[] lineStarts, int offset)
            {
                if (offset < 0 || offset > document.Length) throw new ArgumentOutOfRangeException(nameof(offset));
                File = path; _document = document; Offset = offset;
                var index = Array.BinarySearch(lineStarts, offset);
                if (index < 0) index = ~index - 1;
                var start = lineStarts[index];
                var end = index + 1 < lineStarts.Length ? lineStarts[index + 1] : document.Length;
                Line = document.Substring(start, end - start);
                LineNumber = index + 1;
                ColumnNumber = offset - start + 1;
            }
            public string File { get; }
            public int LineNumber { get; }
            public int ColumnNumber { get; }
            public int Offset { get; }
            public string Line { get; }
            public string GetFullScript() => _document;
        }
    }
}
