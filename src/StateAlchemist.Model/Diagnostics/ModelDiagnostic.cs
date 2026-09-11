using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StateAlchemist.Model;

/// <summary>A problem found in a declaration or a machine.</summary>
public sealed class ModelDiagnostic
{
    /// <summary>Creates a diagnostic.</summary>
    /// <param name="descriptor">The kind of problem.</param>
    /// <param name="location">Where it is.</param>
    /// <param name="arguments">The message's placeholder values.</param>
    public ModelDiagnostic(DiagnosticDescriptor descriptor, SourceSpan location, params object[] arguments)
    {
        Descriptor = descriptor;
        Location = location;
        Arguments = arguments;
    }

    /// <summary>The kind of problem.</summary>
    public DiagnosticDescriptor Descriptor { get; }

    /// <summary>Where it is.</summary>
    public SourceSpan Location { get; }

    /// <summary>The message's placeholder values.</summary>
    public IReadOnlyList<object> Arguments { get; }

    /// <summary>The descriptor's identifier.</summary>
    public string Id => Descriptor.Id;

    /// <summary>The descriptor's severity.</summary>
    public Severity Severity => Descriptor.Severity;

    /// <summary>The formatted message.</summary>
    public string Message => string.Format(CultureInfo.InvariantCulture, Descriptor.MessageFormat, Arguments.ToArray());

    /// <summary>As a compiler would print it.</summary>
    public override string ToString() => $"{Location}: {Severity.ToString().ToLowerInvariant()} {Id}: {Message}";
}
