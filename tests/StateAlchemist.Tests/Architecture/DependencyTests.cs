using System.Linq;
using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Tests.Architecture;

/// <summary>Spec §11: on net8.0 and later the runtime depends on nothing but the framework.</summary>
public class DependencyTests
{
    [Test]
    public async Task RuntimeReferencesOnlyTheFramework()
    {
        var foreign = typeof(IRootState).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => !(name.StartsWith("System", System.StringComparison.Ordinal)
                             || name is "netstandard" or "mscorlib"))
            .ToArray();

        await Assert.That(foreign).IsEmpty();
    }
}
