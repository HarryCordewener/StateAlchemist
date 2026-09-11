using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using StateAlchemist.Model;
using RoslynDescriptor = Microsoft.CodeAnalysis.DiagnosticDescriptor;

namespace StateAlchemist.Generators;

/// <summary>Where a diagnostic goes, as plain values: a <see cref="Location"/> holds a syntax tree and cannot be compared across runs.</summary>
internal sealed record LocationInfo(string Path, TextSpan Span, LinePositionSpan Lines)
{
    public static LocationInfo? From(Location? location) =>
        location is { IsInSource: true } ? new(location.SourceTree!.FilePath, location.SourceSpan, location.GetLineSpan().Span) : null;

    public Location ToLocation() => Location.Create(Path, Span, Lines);
}

/// <summary>A diagnostic as plain values, recreated as a Roslyn <see cref="Diagnostic"/> when reported.</summary>
internal sealed record DiagnosticInfo(string Id, LocationInfo? Location, EquatableArray<string> Arguments) : IEquatable<DiagnosticInfo>
{
    public static DiagnosticInfo From(ModelDiagnostic diagnostic, Location? location) => new(
        diagnostic.Id,
        LocationInfo.From(location),
        new EquatableArray<string>(diagnostic.Arguments.Select(a => Convert.ToString(a, CultureInfo.InvariantCulture) ?? string.Empty).ToArray()));

    public Diagnostic ToDiagnostic() =>
        Diagnostic.Create(Descriptors.For(Id), Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None, Arguments.Cast<object>().ToArray());
}

/// <summary>The catalog's diagnostics as Roslyn descriptors, each linking to its entry in the reference.</summary>
internal static class Descriptors
{
    private const string HelpRoot = "https://github.com/HarryCordewener/StateAlchemist/blob/main/docs/reference/diagnostics.md#";

    private static readonly Dictionary<string, RoslynDescriptor> ById = DiagnosticCatalog.All.ToDictionary(d => d.Id, d => new RoslynDescriptor(
        d.Id,
        d.Title,
        d.MessageFormat,
        "StateAlchemist",
        d.Severity switch
        {
            Severity.Error => DiagnosticSeverity.Error,
            Severity.Warning => DiagnosticSeverity.Warning,
            Severity.Info => DiagnosticSeverity.Info,
            _ => DiagnosticSeverity.Hidden,
        },
        isEnabledByDefault: true,
        helpLinkUri: HelpRoot + d.Id.ToLowerInvariant()));

    public static RoslynDescriptor For(string id) => ById[id];
}
