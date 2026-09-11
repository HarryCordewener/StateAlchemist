namespace StateAlchemist.Model;

/// <summary>Where a declaration is, for diagnostics. A front-end that cannot tell uses <see cref="None"/>.</summary>
public readonly record struct SourceSpan(string File, int Line, int Column)
{
    /// <summary>An unknown location.</summary>
    public static SourceSpan None { get; } = new(string.Empty, 0, 0);

    /// <summary>Whether the location is unknown.</summary>
    public bool IsNone => File.Length == 0;

    /// <inheritdoc/>
    public override string ToString() => IsNone ? "(unknown)" : $"{File}({Line},{Column})";
}
