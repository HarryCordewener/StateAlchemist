using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model.Tests;

internal static class Diagnostics
{
    /// <summary>"SALCH0101: message" per diagnostic, joined with newlines — readable in a failed assertion.</summary>
    public static string Describe(this IEnumerable<ModelDiagnostic> diagnostics) =>
        string.Join("\n", diagnostics.Select(d => $"{d.Id}: {d.Message}"));

    public static string Ids(this IEnumerable<ModelDiagnostic> diagnostics) => string.Join(",", diagnostics.Select(d => d.Id));
}
