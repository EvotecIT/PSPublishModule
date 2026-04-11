using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PowerForge;

internal sealed class YamlTextWriter
{
    private readonly StringBuilder _builder;
    private int _indent;

    public YamlTextWriter(int capacity = 0)
    {
        _builder = capacity > 0 ? new StringBuilder(capacity) : new StringBuilder();
    }

    public IDisposable Indent()
    {
        _indent += 2;
        return new IndentScope(this);
    }

    public void WriteScalar(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("YAML key is required.", nameof(key));

        AppendIndent();
        _builder.Append(key);
        _builder.Append(": ");
        _builder.AppendLine(EscapeScalar(value));
    }

    public void WriteOptionalScalar(string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            WriteScalar(key, value!);
    }

    public void WriteSequence(string key, IReadOnlyList<string>? values)
    {
        if (values is null)
            return;

        var items = values.Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray();
        if (items.Length == 0)
            return;

        WriteKey(key);
        foreach (var item in items)
            WriteSequenceItem(item);
    }

    public void WriteKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("YAML key is required.", nameof(key));

        AppendIndent();
        _builder.Append(key);
        _builder.AppendLine(":");
    }

    public void WriteSequenceItem(string value)
    {
        AppendIndent();
        _builder.Append("- ");
        _builder.AppendLine(EscapeScalar(value));
    }

    public void WriteSequenceItem(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("YAML key is required.", nameof(key));

        AppendIndent();
        _builder.Append("- ");
        _builder.Append(key);
        _builder.Append(": ");
        _builder.AppendLine(EscapeScalar(value));
    }

    public override string ToString() => _builder.ToString();

    internal static string EscapeScalar(string value)
    {
        var normalized = (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", " ").Trim();
        if (normalized.Length == 0)
            return "\"\"";

        return normalized == "-"
            || normalized.StartsWith("- ", StringComparison.Ordinal)
            || normalized.IndexOfAny(new[] { ':', '#', '{', '}', '[', ']', ',', '&', '*', '?', '|', '<', '>', '=', '!', '%', '@', '\\', '"' }) >= 0
            || normalized.Contains(' ')
            ? "\"" + normalized.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
            : normalized;
    }

    private void AppendIndent()
    {
        if (_indent > 0)
            _builder.Append(' ', _indent);
    }

    private sealed class IndentScope : IDisposable
    {
        private readonly YamlTextWriter _writer;
        private bool _disposed;

        public IndentScope(YamlTextWriter writer)
        {
            _writer = writer;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _writer._indent = Math.Max(0, _writer._indent - 2);
            _disposed = true;
        }
    }
}
