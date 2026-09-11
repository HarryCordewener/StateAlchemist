using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using StateAlchemist.Model;
using TUnit.Core;

namespace StateAlchemist.Tests.Docs;

/// <summary>
/// <c>docs/reference/diagnostics.md</c> documents every diagnostic in <see cref="DiagnosticCatalog"/>, with the exact
/// title, severity, place and message — and nothing the catalog does not have. A diagnostic cannot ship undocumented.
/// </summary>
public class DiagnosticsDocumentationTests
{
    private static readonly string Reference = File.ReadAllText(DocsPath()).Replace("\r\n", "\n");

    private static string DocsPath([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "docs", "reference", "diagnostics.md"));

    [Test]
    public async Task EveryDiagnosticHasAnEntryWithItsExactMessage()
    {
        var missing = DiagnosticCatalog.All
            .Where(d => !Reference.Contains($"## {d.Id}\n") || !Reference.Contains($"> {d.MessageFormat}\n"))
            .Select(d => d.Id);
        await Assert.That(string.Join(",", missing)).IsEqualTo("");
    }

    [Test]
    public async Task EveryEntryStatesTitleSeverityAndWhere()
    {
        var wrong = DiagnosticCatalog.All
            .Where(d => !Reference.Contains($"**{d.Title}** · {d.Severity.ToString().ToLowerInvariant()} · {d.ReportedIn}\n"))
            .Select(d => d.Id);
        await Assert.That(string.Join(",", wrong)).IsEqualTo("");
    }

    [Test]
    public async Task EveryDocumentedDiagnosticExists()
    {
        var known = DiagnosticCatalog.All.Select(d => d.Id).ToHashSet();
        var stale = Regex.Matches(Reference, "^## (SALCH[0-9]{4})$", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Where(id => !known.Contains(id));
        await Assert.That(string.Join(",", stale)).IsEqualTo("");
    }
}
