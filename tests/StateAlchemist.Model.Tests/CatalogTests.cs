using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

public class CatalogTests
{
    [Test]
    public async Task EveryIdIsSalchAndFourDigits()
    {
        await Assert.That(DiagnosticCatalog.All.All(d => Regex.IsMatch(d.Id, "^SALCH[0-9]{4}$"))).IsTrue();
    }

    [Test]
    public async Task IdsAreUniqueAndInOrder()
    {
        var ids = DiagnosticCatalog.All.Select(d => d.Id).ToList();
        await Assert.That(ids.Distinct().Count()).IsEqualTo(ids.Count);
        await Assert.That(string.Join(",", ids)).IsEqualTo(string.Join(",", ids.OrderBy(i => i, System.StringComparer.Ordinal)));
    }

    [Test]
    public async Task ADiagnosticFormatsItsMessage()
    {
        var diagnostic = new ModelDiagnostic(DiagnosticCatalog.InitialChild, new SourceSpan("States.cs", 12, 5), "Command", 2);
        await Assert.That(diagnostic.Message).IsEqualTo("State 'Command' has 2 [Initial] children; it needs exactly one");
        await Assert.That(diagnostic.ToString()).IsEqualTo("States.cs(12,5): error SALCH0004: State 'Command' has 2 [Initial] children; it needs exactly one");
    }
}
