using System.Text;

namespace PowerForge.Web;

internal sealed class HtmlFragmentBuilder
{
    private readonly StringBuilder _builder = new();
    private readonly int _indentStep;
    private int _indent;

    public HtmlFragmentBuilder(int initialIndent = 0, int indentStep = 2)
    {
        if (initialIndent < 0)
            throw new ArgumentOutOfRangeException(nameof(initialIndent));
        if (indentStep <= 0)
            throw new ArgumentOutOfRangeException(nameof(indentStep));

        _indent = initialIndent;
        _indentStep = indentStep;
    }

    public bool IsEmpty => _builder.Length == 0;

    public IDisposable Indent()
    {
        _indent += _indentStep;
        return new IndentScope(this);
    }

    public void Line(string? text)
    {
        if (_indent > 0)
            _builder.Append(' ', _indent);
        _builder.AppendLine(text ?? string.Empty);
    }

    public void AppendRaw(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        _builder.Append(text);
    }

    public override string ToString()
        => _builder.ToString();

    private sealed class IndentScope : IDisposable
    {
        private readonly HtmlFragmentBuilder _owner;
        private bool _disposed;

        public IndentScope(HtmlFragmentBuilder owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _owner._indent = Math.Max(0, _owner._indent - _owner._indentStep);
            _disposed = true;
        }
    }
}
