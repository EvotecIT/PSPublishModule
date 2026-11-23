namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Management.Automation;
    using System.Management.Automation.Language;
    using System.Reflection;
    using System.Runtime.ExceptionServices;

    /// <summary>Preserves authored extents while executing only explicitly selected hosted statements.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public sealed class PowerShellHostedRegionSource
    {
        private readonly string _path;
        private readonly string _document;
        private readonly int[] _offsets;

        /// <summary>Creates a source selection from ordered start/end offset pairs.</summary>
        public PowerShellHostedRegionSource(string path, string document, int[] offsets)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _offsets = offsets is null ? throw new ArgumentNullException(nameof(offsets)) : (int[])offsets.Clone();
            if (_offsets.Length == 0 || _offsets.Length % 2 != 0)
                throw new ArgumentException("A region requires ordered statement offset pairs.", nameof(offsets));
            for (var index = 0; index < _offsets.Length; index += 2)
                if (_offsets[index] < 0 || _offsets[index + 1] <= _offsets[index] || _offsets[index + 1] > document.Length ||
                    index > 0 && _offsets[index] < _offsets[index - 1])
                    throw new ArgumentException("A region selection must be ordered, nonoverlapping, and inside its document.", nameof(offsets));
        }

        internal ScriptBlock CreateScriptBlock(string generatedSource)
        {
            var generated = Parser.ParseInput(generatedSource, out _, out var generatedErrors);
            if (generatedErrors.Length != 0 || generated.EndBlock is null)
                throw new ArgumentException("The generated region is not a valid statement block.", nameof(generatedSource));
            var method = typeof(Parser).GetMethod("ParseInput", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(string), typeof(string), typeof(Token[]).MakeByRefType(), typeof(ParseError[]).MakeByRefType() }, null)
                ?? throw new NotSupportedException("PowerShell's authored-source parser is unavailable.");
            var arguments = new object?[] { _document, _path, null, null };
            ScriptBlockAst document;
            try { document = (ScriptBlockAst)method.Invoke(null, arguments)!; }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
            if (((ParseError[])arguments[3]!).Length != 0)
                throw new ArgumentException("The authored source document cannot be parsed.");
            var count = _offsets.Length / 2;
            var prefixCount = generated.EndBlock.Statements.Count - count;
            if (prefixCount < 0) throw new ArgumentException("The generated region has fewer statements than its source selection.");
            var statements = new List<StatementAst>();
            for (var index = 0; index < prefixCount; index++)
                statements.Add((StatementAst)generated.EndBlock.Statements[index].Copy());
            for (var index = 0; index < count; index++)
            {
                var start = _offsets[index * 2];
                var end = _offsets[index * 2 + 1];
                var expected = generated.EndBlock.Statements[prefixCount + index];
                var selected = document.FindAll(node => node is StatementAst && node.GetType() == expected.GetType() &&
                    node.Extent.StartOffset == start && node.Extent.EndOffset == end, searchNestedScriptBlocks: true).SingleOrDefault();
                if (selected is null || selected.Extent.Text != expected.Extent.Text)
                    throw new ArgumentException("The generated region does not match its authored statement selection.");
                statements.Add((StatementAst)selected.Copy());
            }
            // The document is metadata only: no unselected statement enters the executable AST.
            return new ScriptBlockAst(document.Extent, (ParamBlockAst?)generated.ParamBlock?.Copy(),
                new StatementBlockAst(document.Extent, statements, null), isFilter: false).GetScriptBlock();
        }
    }
}
