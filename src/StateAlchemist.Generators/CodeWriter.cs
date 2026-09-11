using System;
using System.Text;

namespace StateAlchemist.Generators;

/// <summary>Writes indented C#: one line at a time, blocks opened and closed in pairs.</summary>
internal sealed class CodeWriter
{
    private readonly StringBuilder _text = new();
    private int _indent;

    public void Line(string line = "")
    {
        if (line.Length > 0)
        {
            _text.Append(' ', _indent * 4).Append(line);
        }

        _text.Append('\n');
    }

    /// <summary>Writes <paramref name="header"/> and <c>{</c>; disposing the result writes the <c>}</c>.</summary>
    public IDisposable Block(string header, string close = "}")
    {
        Line(header);
        Line("{");
        _indent++;
        return new Closer(this, close);
    }

    public override string ToString() => _text.ToString();

    private sealed class Closer(CodeWriter writer, string close) : IDisposable
    {
        public void Dispose()
        {
            writer._indent--;
            writer.Line(close);
        }
    }
}
